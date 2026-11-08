using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Tcp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CunaServer.Tests;

public class IntercomAudioSpikeTests : IDisposable
{
    private readonly TestJwksAuthority _jwks = new();
    private readonly InMemoryFamilyAccountRepository _accountRepo = new();
    private readonly InMemoryDeviceTelemetryRepository _deviceRepo = new();
    private readonly WebApplicationFactory<Program> _factory;

    public IntercomAudioSpikeTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFamilyAccountRepository>();
                services.AddSingleton<IFamilyAccountRepository>(_accountRepo);

                services.RemoveAll<IDeviceTelemetryRepository>();
                services.AddSingleton<IDeviceTelemetryRepository>(_deviceRepo);

                services.RemoveAll<IJwtTokenValidator>();
                services.AddSingleton<IJwtTokenValidator>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    return new SupabaseJwksTokenValidator(new HttpClient(), config, _jwks.JwksJson);
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _jwks.Dispose();
    }

    private HttpClient CreateAuthClient(Guid userId, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _jwks.CreateToken(userId, email));
        return client;
    }

    [Fact]
    public async Task Rf11_EndToEndAudioChunk_DeliversToCentralUnder250Ms_AppliesSafeLimiter_AndRejectsOtherFamily()
    {
        var principalAId = Guid.NewGuid();
        var outsiderBId = Guid.NewGuid();
        using var clientA = CreateAuthClient(principalAId, "mama.a@familia.local");
        using var clientB = CreateAuthClient(outsiderBId, "intruso.b@familia.local");

        // 1. Crear Familia A y Familia B
        var bootARes = await clientA.PostAsJsonAsync("/api/account/bootstrap", new { nombreFamilia = "Familia A" });
        var ctxA = await bootARes.Content.ReadFromJsonAsync<AccountContextResponse>();
        var familyAId = ctxA!.Familia!.Id;

        var bootBRes = await clientB.PostAsJsonAsync("/api/account/bootstrap", new { nombreFamilia = "Familia B" });
        Assert.Equal(HttpStatusCode.Created, bootBRes.StatusCode);

        // Registrar la Unidad Central bajo la Familia A
        var cunaAId = Guid.NewGuid();
        const string centralToken = "TOKEN-CENTRAL-SPIKE-000000000000";
        _deviceRepo.RegisterOrUpdateDevice(new RegisteredDevice(
            Guid.NewGuid(),
            "central-spike-a",
            cunaAId,
            familyAId,
            DeviceTypes.Central,
            CryptoTokenHelper.ComputeSha256Hex(centralToken),
            "vinculado",
            null,
            0,
            null,
            "1.0.0-sim"));

        var tcpServer = _factory.Services.GetRequiredService<DeviceTcpServer>();
        var tcpPort = await tcpServer.WaitForBoundPortAsync();

        // 2. Conectar simulador de Unidad Central por TCP y autenticar con hello
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, tcpPort);
        await using var tcpStream = tcpClient.GetStream();

        var helloJson = JsonSerializer.Serialize(new
        {
            v = 1,
            t = "hello",
            seq = 0,
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            d = new
            {
                device_id = "central-spike-a",
                device_type = "central",
                fw = "1.0.0-sim",
                token = centralToken
            }
        }) + "\n";
        await tcpStream.WriteAsync(Encoding.UTF8.GetBytes(helloJson));
        await tcpStream.FlushAsync();

        var (ackLine, _, _) = await TcpProtocolParser.ReadBoundedLineAsync(tcpStream, CancellationToken.None);
        Assert.NotNull(ackLine);
        Assert.Contains("hello_ack", ackLine!);

        // 3. Medir latencia extremo a extremo (HTTP -> Backend -> TCP Central) <= 250 ms (RF11)
        var rawPcm = Enumerable.Repeat((byte)255, 800).ToArray(); // 100 ms a 8 kHz con pico máximo (255)
        var sw = Stopwatch.StartNew();

        using var postRes = await clientA.PostAsJsonAsync($"/api/cribs/{cunaAId}/intercom/chunk", new
        {
            sampleRateHz = 8000,
            seq = 1,
            pcmBase64 = Convert.ToBase64String(rawPcm)
        });
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        var (cmdLine, exceeded4Kb, _) = await TcpProtocolParser.ReadBoundedLineAsync(tcpStream, CancellationToken.None);
        sw.Stop();

        Assert.False(exceeded4Kb);
        Assert.NotNull(cmdLine);
        Assert.True(sw.ElapsedMilliseconds < 250, $"Latencia RF11 excedida: {sw.ElapsedMilliseconds} ms");

        using var cmdDoc = JsonDocument.Parse(cmdLine!);
        Assert.Equal("command", cmdDoc.RootElement.GetProperty("t").GetString());
        Assert.Equal("audio_chunk", cmdDoc.RootElement.GetProperty("d").GetProperty("cmd").GetString());

        var receivedB64 = cmdDoc.RootElement.GetProperty("d").GetProperty("params").GetProperty("pcm_b64").GetString()!;
        var receivedPcm = Convert.FromBase64String(receivedB64);
        Assert.Equal(800, receivedPcm.Length);
        // Verificar que el limitador de seguridad auditiva (<= 65 dBA) redujo el pico de 255 a ~211
        Assert.All(receivedPcm, sample => Assert.InRange(sample, 45, 211));

        // 4. Usuario de Familia B intenta transmitir a la cuna de Familia A -> 403 Forbidden + bitacora_seguridad
        using var forbiddenRes = await clientB.PostAsJsonAsync($"/api/cribs/{cunaAId}/intercom/chunk", new
        {
            sampleRateHz = 8000,
            seq = 1,
            pcmBase64 = Convert.ToBase64String(rawPcm)
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        // 5. Bloque PCM > 1280 bytes se rechaza con 400 Bad Request
        var oversizedPcm = new byte[1500];
        using var badSizeRes = await clientA.PostAsJsonAsync($"/api/cribs/{cunaAId}/intercom/chunk", new
        {
            sampleRateHz = 8000,
            seq = 2,
            pcmBase64 = Convert.ToBase64String(oversizedPcm)
        });
        Assert.Equal(HttpStatusCode.BadRequest, badSizeRes.StatusCode);
    }
}
