using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Tcp;
using CunaServer.Telegram;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CunaServer.Tests;

/// <summary>
/// Pruebas de rendimiento y presupuestos de latencia/rendimiento del sistema (TCP, JWT JWKS, HTTP /live, Export RF32).
/// </summary>
public sealed class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Benchmark_TcpLineReader_ThroughputExceedsThreshold()
    {
        const int lineCount = 10_000;
        var sampleJson =
            """{"v":1,"t":"telemetry","seq":123,"ts":1762550000,"d":{"device_id":"sim-colchon-a","cuna_id":"aaaaaaaa-c001-4000-8000-000000000001","temp_c":23.4,"humedad":51.2,"masa_kg":6.120,"indice_movimiento":0.042}}"""
            + "\n";
        var sampleBytes = Encoding.UTF8.GetBytes(sampleJson);
        await using var stream = new MemoryStream(sampleBytes.Length * lineCount);
        for (var i = 0; i < lineCount; i++)
        {
            stream.Write(sampleBytes);
        }
        stream.Position = 0;

        using var reader = new TcpLineReader(stream);
        var sw = Stopwatch.StartNew();
        var readLines = 0;
        while (true)
        {
            var (line, exceeded, eos) = await reader.ReadLineAsync(CancellationToken.None);
            if (eos)
            {
                break;
            }

            Assert.False(exceeded);
            Assert.NotNull(line);
            readLines++;
        }
        sw.Stop();

        var linesPerSec = readLines / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
        _output.WriteLine($"[PERF] TcpLineReader: {readLines} líneas en {sw.Elapsed.TotalMilliseconds:F2} ms ({linesPerSec:N0} líneas/s)");

        Assert.Equal(lineCount, readLines);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Lectura TCP demasiado lenta: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Benchmark_CachedJwksJwtValidation_ThroughputExceedsThreshold()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        const string kid = "perf-key-1";
        var jwksJson = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    kid,
                    x = CryptoTokenHelper.Base64UrlEncode(pub.Q.X!),
                    y = CryptoTokenHelper.Base64UrlEncode(pub.Q.Y!)
                }
            }
        });

        var userId = Guid.NewGuid();
        var headerB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { alg = "ES256", typ = "JWT", kid })));
        var payloadB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                sub = userId.ToString(),
                email = "perf@cuna.local",
                exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            })));
        var signingInput = $"{headerB64}.{payloadB64}";
        var sig = ecdsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var bearerHeader = $"Bearer {signingInput}.{CryptoTokenHelper.Base64UrlEncode(sig)}";

        var validator = new SupabaseJwksTokenValidator(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            jwksJson);

        // Warmup
        var first = await validator.ValidateBearerTokenAsync(bearerHeader, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(userId, first!.UserId);

        const int iterations = 2000;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var identity = await validator.ValidateBearerTokenAsync(bearerHeader, CancellationToken.None);
            Assert.NotNull(identity);
        }
        sw.Stop();

        var opsPerSec = iterations / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
        _output.WriteLine($"[PERF] JWT ES256 (JWKS cacheada): {iterations} verificaciones en {sw.Elapsed.TotalMilliseconds:F2} ms ({opsPerSec:N0} ops/s)");

        Assert.True(sw.ElapsedMilliseconds < 4000, $"Validación JWT demasiado lenta: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Benchmark_LivePollingAndHistoryExport_MeetLatencyBudgets()
    {
        var accRepo = new InMemoryFamilyAccountRepository();
        var devRepo = new InMemoryDeviceTelemetryRepository();
        var tgTransport = new InMemoryTelegramBotTransport();
        var principalId = Guid.Parse("77777777-7777-7777-7777-777777777701");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IFamilyAccountRepository>(accRepo);
                services.AddSingleton<IDeviceTelemetryRepository>(devRepo);
                services.AddSingleton<ITelegramBotTransport>(tgTransport);
                services.AddSingleton<IJwtTokenValidator>(new BenchmarkJwtValidator(principalId));
            });
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "perf-token");

        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Perf", "Principal", "P1"));
        var pairingResp = await client.PostAsync("/api/pairing/window", null);
        pairingResp.EnsureSuccessStatusCode();
        var claimResp = await client.PostAsJsonAsync("/api/pairing/claim",
            new ClaimDeviceRequest("pico-central-nuevo", "RECLAMO-CENTRAL-1001", null, "Cuna Benchmark"));
        claimResp.EnsureSuccessStatusCode();

        var cribs = await client.GetFromJsonAsync<List<CribSummaryResponse>>("/api/cribs");
        var cunaId = cribs![0].Cuna.Id;

        var babyResp = await client.PostAsJsonAsync(
            "/api/babies",
            new UpsertBabyRequest(cunaId, "Sofía", new DateOnly(2026, 6, 1), null));
        babyResp.EnsureSuccessStatusCode();
        var baby = await babyResp.Content.ReadFromJsonAsync<BabyRecord>();

        var now = DateTimeOffset.UtcNow;
        var fakeDeviceRowId = Guid.NewGuid();
        for (var i = 1; i <= 500; i++)
        {
            await devRepo.SaveTelemetryAsync(
                new TelemetryRecord(cunaId, fakeDeviceRowId, now.AddSeconds(-500 + i), 6.2m, 0.03m, 23.5m, 48.0m, null, null, 90, i),
                CancellationToken.None);
        }

        for (var i = 0; i < 60; i++)
        {
            await devRepo.CreateSleepSessionAsync(
                baby!.Id, cunaId, i % 2 == 0 ? "siesta" : "noche", now.AddHours(-i - 2), now.AddHours(-i - 1), 0.04m, 0, CancellationToken.None);
            var feed = await devRepo.CreateFeedingSessionAsync(
                baby.Id, null, 180m, 36.8m, false, now.AddHours(-i - 1), CancellationToken.None);
            await devRepo.CompleteFeedingSessionAsync(feed.Id, 95m, 85m, now.AddHours(-i - 1).AddMinutes(12), CancellationToken.None);
        }

        // Benchmark 50 consultas HTTP GET /live
        var liveSw = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            var resp = await client.GetAsync($"/api/cribs/{cunaId}/live");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        liveSw.Stop();
        var avgLiveMs = liveSw.Elapsed.TotalMilliseconds / 50.0;
        _output.WriteLine($"[PERF] HTTP GET /live: 50 consultas en {liveSw.Elapsed.TotalMilliseconds:F2} ms (promedio {avgLiveMs:F2} ms/req)");
        Assert.True(avgLiveMs < 100.0, $"GET /live promedio demasiado lento: {avgLiveMs:F2} ms");

        // Benchmark exportación CSV + PDF (RF32 exige < 10 s)
        var exportSw = Stopwatch.StartNew();
        var csvResp = await client.PostAsJsonAsync($"/api/babies/{baby!.Id}/history/export", new ExportHistoryRequest("csv"));
        var pdfResp = await client.PostAsJsonAsync($"/api/babies/{baby!.Id}/history/export", new ExportHistoryRequest("pdf"));
        exportSw.Stop();

        Assert.Equal(HttpStatusCode.OK, csvResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pdfResp.StatusCode);
        _output.WriteLine($"[PERF] Export RF32 (CSV + PDF, 120 registros): {exportSw.Elapsed.TotalMilliseconds:F2} ms (presupuesto < 10000 ms)");
        Assert.True(exportSw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void Benchmark_TcpProtocolParser_TelemetryAndEventEnvelopeThroughput()
    {
        const int iterations = 10_000;
        var cunaId = Guid.NewGuid();
        var centralDeviceId = Guid.NewGuid();
        var colchonDeviceId = Guid.NewGuid();

        // Warmup
        var warmTelemetry = new
        {
            v = 1,
            t = "telemetry",
            seq = 1L,
            ts = 1762550000L,
            d = new { temp_c = 23.5m, hum_pct = 52.0m, battery_pct = 88 }
        };
        var warmJson = JsonSerializer.Serialize(warmTelemetry);
        Assert.True(TcpProtocolParser.TryParseEnvelope(warmJson, out var warmEnv, out _, out _));
        Assert.True(TcpProtocolParser.TryValidateTelemetry(
            DeviceTypes.Central, cunaId, centralDeviceId, warmEnv!.Seq,
            DateTimeOffset.FromUnixTimeSeconds(warmEnv.Ts ?? 0), warmEnv.D,
            out var warmRecord, out _));
        Assert.NotNull(warmRecord);

        var sw = Stopwatch.StartNew();
        var processed = 0;

        for (var i = 0; i < iterations; i++)
        {
            if ((i & 1) == 0)
            {
                var envelope = new
                {
                    v = 1,
                    t = "telemetry",
                    seq = (long)i,
                    ts = 1762550000L + i,
                    d = new
                    {
                        temp_c = 20.0m + (i % 100) * 0.1m,
                        hum_pct = 40.0m + (i % 200) * 0.1m,
                        battery_pct = 50 + (i % 50)
                    }
                };

                var serialized = JsonSerializer.Serialize(envelope);
                if (!TcpProtocolParser.TryParseEnvelope(serialized, out var parsed, out _, out _) || parsed is null)
                {
                    continue;
                }

                if (TcpProtocolParser.TryValidateTelemetry(
                    DeviceTypes.Central,
                    cunaId,
                    centralDeviceId,
                    parsed.Seq,
                    DateTimeOffset.FromUnixTimeSeconds(parsed.Ts ?? 0),
                    parsed.D,
                    out var record,
                    out _) && record is not null)
                {
                    processed++;
                }
            }
            else
            {
                var envelope = new
                {
                    v = 1,
                    t = "event",
                    seq = (long)i,
                    ts = 1762550000L + i,
                    d = new
                    {
                        kind = (i % 4) switch
                        {
                            0 => "removal",
                            1 => "env_out_of_range",
                            2 => "movement_high",
                            _ => "button_attention"
                        },
                        detalles = "alerta de prueba en benchmark",
                        source_seq = i
                    }
                };

                var serialized = JsonSerializer.Serialize(envelope);
                if (!TcpProtocolParser.TryParseEnvelope(serialized, out var parsed, out _, out _) || parsed is null)
                {
                    continue;
                }

                if (TcpProtocolParser.TryValidateEvent(
                    cunaId,
                    colchonDeviceId,
                    DateTimeOffset.FromUnixTimeSeconds(parsed.Ts ?? 0),
                    parsed.D,
                    out var alert,
                    out _,
                    out _) && alert is not null)
                {
                    processed++;
                }
            }
        }
        sw.Stop();

        var opsPerSec = processed / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
        _output.WriteLine($"[PERF] TcpProtocolParser Telemetría + Eventos: {processed} sobres en {sw.Elapsed.TotalMilliseconds:F2} ms ({opsPerSec:N0} sobres/s)");

        Assert.Equal(iterations, processed);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Procesamiento de sobres demasiado lento: {sw.ElapsedMilliseconds} ms (presupuesto < 1500 ms)");
    }

    private sealed class BenchmarkJwtValidator(Guid userId) : IJwtTokenValidator
    {
        public Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct)
            => Task.FromResult<AuthenticatedIdentity?>(new AuthenticatedIdentity(userId, "perf@cuna.local"));
    }
}
