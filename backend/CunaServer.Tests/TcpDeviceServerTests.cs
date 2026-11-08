using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Tcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace CunaServer.Tests;

public class TcpDeviceServerTests : IAsyncLifetime
{
    private readonly InMemoryDeviceTelemetryRepository _repository = new();
    private readonly DeviceTcpServer _server;
    private readonly CancellationTokenSource _cts = new();
    private int _port;

    public TcpDeviceServerTests()
    {
        var options = new DeviceTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0, // Puerto efímero libre para pruebas
            HelloTimeout = TimeSpan.FromMilliseconds(600),
            AckTimeout = TimeSpan.FromMilliseconds(400),
            IpBlockDuration = TimeSpan.FromSeconds(30),
            MaxFailedHelloAttempts = 3
        };

        _server = new DeviceTcpServer(_repository, NullLogger<DeviceTcpServer>.Instance, options);
    }

    public async Task InitializeAsync()
    {
        await _server.StartAsync(_cts.Token);
        _port = await _server.WaitForBoundPortAsync(_cts.Token);
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _server.StopAsync(CancellationToken.None);
        _server.Dispose();
        _cts.Dispose();
    }

    private static async Task SendLineAsync(Stream stream, object payload)
    {
        var json = JsonSerializer.Serialize(payload) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<JsonDocument?> ReadJsonLineAsync(Stream stream, int timeoutMs = 2000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var (line, _, eos) = await TcpProtocolParser.ReadBoundedLineAsync(stream, cts.Token);
        if (eos || string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        return JsonDocument.Parse(line);
    }

    [Fact]
    public async Task Connect_Hello_Telemetry_Events_DeduplicationBySeq_AndIgnoreClientSpoofedIds()
    {
        var cunaId = Guid.Parse("aaaaaaaa-c001-4000-8000-000000000001");
        var spoofedCunaId = Guid.Parse("bbbbbbbb-c001-4000-8000-000000000001");

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, _port);
            await using var stream = client.GetStream();

            await SendLineAsync(stream, new
            {
                v = 1,
                t = "hello",
                seq = 0,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                d = new
                {
                    device_id = "sim-colchon-a",
                    device_type = "colchon",
                    fw = "1.0.0-sim",
                    token = "TOKEN-COLCHON-A-DEMO-000000000000"
                }
            });

            using var helloAck = await ReadJsonLineAsync(stream);
            Assert.NotNull(helloAck);
            Assert.Equal("hello_ack", helloAck!.RootElement.GetProperty("t").GetString());
            Assert.True(helloAck.RootElement.GetProperty("d").GetProperty("session_ok").GetBoolean());
            Assert.Equal(0.20m, helloAck.RootElement.GetProperty("d").GetProperty("config").GetProperty("mass_scale").GetDecimal());

            // Enviar telemetría con seq=1 e intentando falsificar cuna_id y family_id (Regla 4 AGENTS.md)
            await SendLineAsync(stream, new
            {
                v = 1,
                t = "telemetry",
                seq = 1,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                d = new
                {
                    family_id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                    cuna_id = spoofedCunaId,
                    mass_kg = 0.640m,
                    movement_idx = 0.025m
                }
            });

            using var ack1 = await ReadJsonLineAsync(stream);
            Assert.NotNull(ack1);
            Assert.Equal("ack", ack1!.RootElement.GetProperty("t").GetString());

            // Reenviar el mismo seq=1 (simulando reconexión / búfer offline) -> no debe duplicar fila
            await SendLineAsync(stream, new
            {
                v = 1,
                t = "telemetry",
                seq = 1,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                d = new
                {
                    mass_kg = 0.640m,
                    movement_idx = 0.025m
                }
            });
            using var ackDup = await ReadJsonLineAsync(stream);
            Assert.NotNull(ackDup);
            Assert.Equal("ack", ackDup!.RootElement.GetProperty("t").GetString());

            // Enviar evento removal con seq=2
            await SendLineAsync(stream, new
            {
                v = 1,
                t = "event",
                seq = 2,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                d = new
                {
                    kind = "removal",
                    mass_kg = 0.050m
                }
            });
            using var ack2 = await ReadJsonLineAsync(stream);
            Assert.NotNull(ack2);
            Assert.Equal("ack", ack2!.RootElement.GetProperty("t").GetString());
        }

        var rows = await _repository.ListTelemetryForCribAsync(cunaId, CancellationToken.None);
        Assert.Single(rows);
        Assert.Equal(cunaId, rows[0].CunaId);
        Assert.Equal(0.640m, rows[0].MasaKg);

        var spoofedRows = await _repository.ListTelemetryForCribAsync(spoofedCunaId, CancellationToken.None);
        Assert.Empty(spoofedRows);

        var alerts = await _repository.ListAlertsForCribAsync(cunaId, CancellationToken.None);
        Assert.Single(alerts);
        Assert.Equal("removal", alerts[0].Tipo);
        Assert.Equal("critica", alerts[0].Severidad);
    }

    [Fact]
    public async Task Security_RejectsLineOver4Kb_OutOfRangeValues_AndBlocksAfter3FailedHellos()
    {
        // 1. Línea mayor a 4 KB es rechazada y registrada
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, _port);
            await using var stream = client.GetStream();

            var oversized = new string('X', 4500) + "\n";
            var bytes = Encoding.UTF8.GetBytes(oversized);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var errDoc = await ReadJsonLineAsync(stream);
            Assert.NotNull(errDoc);
            Assert.Equal("error", errDoc!.RootElement.GetProperty("t").GetString());
            Assert.Equal("line_too_long", errDoc.RootElement.GetProperty("d").GetProperty("code").GetString());
        }

        // 2. 3 intentos fallidos de hello cierran la sesión y bloquean temporalmente la IP
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, _port);
            await using var stream = client.GetStream();

            for (var i = 1; i <= 3; i++)
            {
                await SendLineAsync(stream, new
                {
                    v = 1,
                    t = "hello",
                    seq = 0,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    d = new
                    {
                        device_id = "sim-central-a",
                        device_type = "central",
                        fw = "1.0.0",
                        token = $"TOKEN-INVALIDO-INTENTO-{i}-000000"
                    }
                });

                using var err = await ReadJsonLineAsync(stream);
                Assert.NotNull(err);
                Assert.Equal("auth_failed", err!.RootElement.GetProperty("d").GetProperty("code").GetString());
            }
        }

        var audit = _repository.GetSecurityAuditSnapshot();
        Assert.Contains(audit, e => e.Evento == "tcp_mensaje_rechazado" && e.Detalle == "linea_excede_4kb");
        Assert.Contains(audit, e => e.Evento == "hello_bloqueo_ip");
    }

    [Fact]
    public async Task CommandDispatch_WaitsForDeviceAck()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = client.GetStream();

        await SendLineAsync(stream, new
        {
            v = 1,
            t = "hello",
            seq = 0,
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            d = new
            {
                device_id = "sim-central-a",
                device_type = "central",
                fw = "1.0.0-sim",
                token = "TOKEN-CENTRAL-A-DEMO-000000000000"
            }
        });

        using var helloAck = await ReadJsonLineAsync(stream);
        Assert.NotNull(helloAck);

        // Tarea del dispositivo que escucha el comando y responde ack
        var deviceResponder = Task.Run(async () =>
        {
            using var cmdDoc = await ReadJsonLineAsync(stream);
            Assert.NotNull(cmdDoc);
            Assert.Equal("command", cmdDoc!.RootElement.GetProperty("t").GetString());
            var cmdId = cmdDoc.RootElement.GetProperty("d").GetProperty("id").GetString()!;

            await SendLineAsync(stream, new
            {
                v = 1,
                t = "ack",
                seq = 1,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                d = new { id = cmdId, ok = true }
            });
        });

        var acked = await _server.SendCommandAsync(
            "sim-central-a",
            "light_set",
            new { on = true, level = 60 },
            requireAck: true);

        await deviceResponder;
        Assert.True(acked);
    }

    [Fact]
    public async Task PythonSimulators_AllFiveDevicesConnectAndPersistDataAndPassUnitTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var repoRoot = dir!.FullName;

        // 1. Ejecutar suite unitaria de Python (simulators/test_simulators.py)
        var unitTestPsi = new ProcessStartInfo("python3", "-m unittest discover -s simulators -p test_*.py")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var unitProc = Process.Start(unitTestPsi)!)
        {
            await unitProc.WaitForExitAsync();
            var stderr = await unitProc.StandardError.ReadToEndAsync();
            Assert.True(unitProc.ExitCode == 0, $"Fallo en test_simulators.py: {stderr}");
        }

        // 2. Ejecutar los 5 simuladores reales contra DeviceTcpServer (--device all --once)
        var simPsi = new ProcessStartInfo(
            "python3",
            $"simulators/run_simulators.py --host 127.0.0.1 --port {_port} --device all --once")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var simProc = Process.Start(simPsi)!)
        {
            await simProc.WaitForExitAsync();
            var stderr = await simProc.StandardError.ReadToEndAsync();
            Assert.True(simProc.ExitCode == 0, $"Fallo en run_simulators.py: {stderr}");
        }

        var cunaId = Guid.Parse("aaaaaaaa-c001-4000-8000-000000000001");
        var telemetry = await _repository.ListTelemetryForCribAsync(cunaId, CancellationToken.None);
        var toyUsage = await _repository.ListToyUsageForCribAsync(cunaId, CancellationToken.None);

        // Central, colchón y biberón emitieron telemetría; móvil y juguete emitieron eventos de uso
        Assert.Equal(3, telemetry.Count);
        Assert.Equal(2, toyUsage.Count);
    }
}
