using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Tcp;

/// <summary>
/// Opciones de configuración y límites de seguridad del servidor TCP local para nodos Pico W.
/// </summary>
public sealed class DeviceTcpServerOptions
{
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;
    public int Port { get; set; } = 9000;

    /// <summary>
    /// Ventana máxima (5 s por defecto) para completar el handshake <c>hello</c>; evita que
    /// clientes lentos u ociosos agoten descriptores de socket antes de autenticarse.
    /// </summary>
    public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan IpBlockDuration { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxFailedHelloAttempts { get; set; } = 3;
}

/// <summary>
/// Servidor TCP en segundo plano que atiende conexiones persistentes de las 5 Raspberry Pi Pico W
/// en red local usando tramas JSON delimitadas por <c>\n</c> (máximo 4 KB por línea).
/// </summary>
public sealed class DeviceTcpServer : BackgroundService
{
    private readonly IDeviceTelemetryRepository _repository;
    private readonly ILogger<DeviceTcpServer> _logger;
    private readonly DeviceTcpServerOptions _options;
    private TcpListener? _listener;

    private readonly ConcurrentDictionary<string, ConnectedDeviceSession> _activeSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IpFailureState> _ipFailures = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<int> _boundPortTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<RegisteredDevice, EventAlertRecord, CancellationToken, Task<bool>>? DeviceAlertInterceptor { get; set; }

    public DeviceTcpServer(
        IDeviceTelemetryRepository repository,
        ILogger<DeviceTcpServer> logger,
        DeviceTcpServerOptions? options = null,
        IConfiguration? configuration = null)
    {
        _repository = repository;
        _logger = logger;
        _options = options ?? BuildOptionsFromConfig(configuration);
    }

    public Task<int> WaitForBoundPortAsync(CancellationToken ct = default)
        => _boundPortTcs.Task.WaitAsync(ct);

    public bool IsDeviceConnected(string deviceId)
        => _activeSessions.ContainsKey(deviceId);

    public int ConnectedDeviceCount => _activeSessions.Count;

    public bool DisconnectDevice(string deviceId)
    {
        if (!_activeSessions.TryRemove(deviceId, out var session))
        {
            return false;
        }

        session.Close();
        return true;
    }

    /// <summary>
    /// Envía un comando JSON al dispositivo conectado; si <paramref name="requireAck"/> es verdadero,
    /// espera confirmación (<c>ack</c>) hasta <c>AckTimeout</c> con hasta 3 reintentos.
    /// </summary>
    public async Task<bool> SendCommandAsync(
        string deviceId,
        string cmd,
        object parameters,
        bool requireAck = true,
        CancellationToken ct = default)
    {
        if (!_activeSessions.TryGetValue(deviceId, out var session))
        {
            return false;
        }

        var cmdId = Guid.NewGuid().ToString("N")[..12];
        var maxAttempts = requireAck ? 3 : 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var ackTask = requireAck ? session.RegisterPendingAck(cmdId) : Task.FromResult(true);
            var envelope = BuildEnvelope(session, "command", DateTimeOffset.UtcNow, new
            {
                id = cmdId,
                cmd,
                @params = parameters
            });

            if (!await session.TrySendJsonLineAsync(envelope, ct))
            {
                session.CancelPendingAck(cmdId);
                return false;
            }

            if (!requireAck)
            {
                return true;
            }

            if (await WaitForCommandAckAsync(session, cmdId, ackTask, ct))
            {
                return true;
            }
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(_options.BindAddress, _options.Port);
        _listener.Start();

        var boundEndpoint = (IPEndPoint)_listener.LocalEndpoint;
        _boundPortTcs.TrySetResult(boundEndpoint.Port);
        _logger.LogInformation("Servidor TCP de dispositivos escuchando en {Address}:{Port}", boundEndpoint.Address, boundEndpoint.Port);

        var clientTasks = new List<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(stoppingToken);
                clientTasks.Add(HandleClientSafeAsync(tcpClient, stoppingToken));
                clientTasks.RemoveAll(t => t.IsCompleted);
            }
        }
        catch (OperationCanceledException)
        {
            // Cierre normal
        }
        finally
        {
            await ShutdownListenerAndSessionsAsync(clientTasks);
        }
    }

    private async Task<bool> WaitForCommandAckAsync(
        ConnectedDeviceSession session,
        string cmdId,
        Task<bool> ackTask,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.AckTimeout);
            return await ackTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            session.CancelPendingAck(cmdId);
            return false;
        }
    }

    private async Task ShutdownListenerAndSessionsAsync(List<Task> clientTasks)
    {
        foreach (var session in _activeSessions.Values)
        {
            session.Close();
        }
        _activeSessions.Clear();
        _listener?.Stop();

        try
        {
            await Task.WhenAll(clientTasks);
        }
        catch
        {
            // Ignorar excepciones de cierre
        }
    }

    private async Task HandleClientSafeAsync(TcpClient client, CancellationToken ct)
    {
        var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

        using (client)
        {
            if (IsIpTemporarilyBlocked(remoteIp))
            {
                return;
            }

            await using var stream = client.GetStream();
            using var lineReader = new TcpLineReader(stream);
            var session = new ConnectedDeviceSession(client, stream);
            RegisteredDevice? authenticatedDevice = null;

            try
            {
                authenticatedDevice = await AuthenticateDeviceAsync(lineReader, session, remoteIp, ct);
                if (authenticatedDevice is null)
                {
                    return;
                }

                await RunAuthenticatedMessageLoopAsync(lineReader, session, authenticatedDevice, remoteIp, ct);
            }
            finally
            {
                if (authenticatedDevice is not null)
                {
                    _activeSessions.TryRemove(
                        new KeyValuePair<string, ConnectedDeviceSession>(authenticatedDevice.DeviceId, session));
                }
                session.Close();
            }
        }
    }

    /// <summary>
    /// Exige un handshake <c>hello</c> válido dentro de <see cref="DeviceTcpServerOptions.HelloTimeout"/> (5 s);
    /// protege el servidor local contra sockets colgados o fuerza bruta por IP.
    /// </summary>
    private async Task<RegisteredDevice?> AuthenticateDeviceAsync(
        TcpLineReader lineReader,
        ConnectedDeviceSession session,
        string remoteIp,
        CancellationToken ct)
    {
        using var helloTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        helloTimeoutCts.CancelAfter(_options.HelloTimeout);

        var failedHelloCount = 0;

        while (true)
        {
            string? rawLine;
            bool exceeded;
            bool eos;
            try
            {
                (rawLine, exceeded, eos) = await lineReader.ReadLineAsync(helloTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await SendErrorAsync(session, "hello_timeout", "Tiempo de espera de hello (5 s) agotado.", ct);
                return null;
            }

            if (eos)
            {
                return null;
            }

            if (exceeded)
            {
                await RecordValidationFailureAsync(null, null, remoteIp, "linea_excede_4kb", ct);
                await SendErrorAsync(session, "line_too_long", "La línea supera el límite de 4 KB.", ct);
                return null;
            }

            var (device, shouldClose) = await TryAuthenticateHelloLineAsync(
                session,
                rawLine!,
                remoteIp,
                ++failedHelloCount,
                ct);

            if (device is not null)
            {
                return device;
            }

            if (shouldClose)
            {
                return null;
            }
        }
    }

    private async Task<(RegisteredDevice? Device, bool ShouldClose)> TryAuthenticateHelloLineAsync(
        ConnectedDeviceSession session,
        string rawLine,
        string remoteIp,
        int attemptNumber,
        CancellationToken ct)
    {
        if (!TcpProtocolParser.TryParseEnvelope(rawLine, out var env, out var errCode, out var errMsg))
        {
            await RecordValidationFailureAsync(null, null, remoteIp, $"sobre_invalido:{errCode}", ct);
            if (errCode == "unsupported_version")
            {
                await SendErrorAsync(session, errCode ?? "invalid_envelope", errMsg ?? "Mensaje inválido.", ct);
                return (null, true);
            }

            var blocked = await HandleHelloFailureAsync(
                session,
                remoteIp,
                attemptNumber,
                errCode ?? "invalid_envelope",
                errMsg ?? "Mensaje inválido.",
                ct);
            return (null, blocked);
        }

        if (env!.T != "hello")
        {
            var blocked = await HandleHelloFailureAsync(
                session,
                remoteIp,
                attemptNumber,
                "hello_required",
                "El primer mensaje debe ser 'hello'.",
                ct);
            return (null, blocked);
        }

        if (!TcpProtocolParser.TryValidateHello(
                env.D,
                out var deviceId,
                out var deviceType,
                out var fwVersion,
                out var rawToken,
                out var helloErr))
        {
            var blocked = await HandleHelloFailureAsync(
                session,
                remoteIp,
                attemptNumber,
                "invalid_hello",
                helloErr ?? "Datos de hello inválidos.",
                ct);
            return (null, blocked);
        }

        var candidateDevice = await _repository.GetDeviceByDeviceIdAsync(deviceId, ct);
        var tokenHash = CryptoTokenHelper.ComputeSha256Hex(rawToken);
        var isValidDevice =
            candidateDevice is not null &&
            string.Equals(candidateDevice.Tipo, deviceType, StringComparison.Ordinal) &&
            candidateDevice.Estado is not ("revocado" or "sin_dueno") &&
            CryptoTokenHelper.ConstantTimeHashEquals(candidateDevice.HashToken, tokenHash);

        if (!isValidDevice)
        {
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                candidateDevice?.FamilyId,
                null,
                candidateDevice?.Id,
                "hello_fallido",
                remoteIp,
                $"device_id={deviceId}",
                DateTimeOffset.UtcNow), ct);

            var blocked = await HandleHelloFailureAsync(
                session,
                remoteIp,
                attemptNumber,
                "auth_failed",
                "Autenticación de dispositivo rechazada.",
                ct);
            return (null, blocked);
        }

        ClearIpFailures(remoteIp);
        var authenticatedDevice = candidateDevice!;
        RegisterActiveSession(authenticatedDevice, session);
        await SendHelloAckAsync(session, authenticatedDevice, fwVersion, ct);
        return (authenticatedDevice, false);
    }

    private void RegisterActiveSession(RegisteredDevice device, ConnectedDeviceSession session)
    {
        session.DeviceId = device.DeviceId;
        if (_activeSessions.TryRemove(device.DeviceId, out var oldSession))
        {
            oldSession.Close();
        }
        _activeSessions[device.DeviceId] = session;
    }

    /// <summary>
    /// Registra el latido inicial y envía <c>hello_ack</c> con <c>mass_scale</c> (por defecto 0.2)
    /// y los umbrales vigentes de la cuna para sincronizar el firmware inmediatamente tras reconectar.
    /// </summary>
    private async Task SendHelloAckAsync(
        ConnectedDeviceSession session,
        RegisteredDevice device,
        string fwVersion,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.RecordHeartbeatAsync(
            device.Id,
            device.CunaId,
            device.Tipo,
            device.BateriaPct,
            fwVersion,
            now,
            ct);

        var cribConfig = await _repository.GetCribConfigAsync(device.CunaId, device.FamilyId, ct);
        var helloAck = BuildEnvelope(session, "hello_ack", now, new
        {
            server_time = now.ToUnixTimeSeconds(),
            session_ok = true,
            last_seq = device.UltimoSeq,
            config = new
            {
                mass_scale = cribConfig.EscalaMasa,
                temp_min = cribConfig.TempMin,
                temp_max = cribConfig.TempMax,
                hum_min = cribConfig.HumMin,
                hum_max = cribConfig.HumMax,
                movement_threshold = cribConfig.MovimientoUmbral,
                movement_alert_enabled = cribConfig.MovimientoAlertaActiva,
                mass_noise_floor = cribConfig.RuidoMasaPiso
            }
        });

        await session.TrySendJsonLineAsync(helloAck, ct);
    }

    private async Task RunAuthenticatedMessageLoopAsync(
        TcpLineReader lineReader,
        ConnectedDeviceSession session,
        RegisteredDevice device,
        string remoteIp,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (rawLine, exceeded, eos) = await lineReader.ReadLineAsync(ct);
            if (eos)
            {
                break;
            }

            if (exceeded)
            {
                await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "linea_excede_4kb", ct);
                await SendErrorAsync(session, "line_too_long", "La línea supera el límite de 4 KB.", ct);
                continue;
            }

            if (!TcpProtocolParser.TryParseEnvelope(rawLine!, out var env, out var errCode, out var errMsg))
            {
                await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, $"sobre_invalido:{errCode}", ct);
                await SendErrorAsync(session, errCode ?? "invalid_envelope", errMsg ?? "Mensaje inválido.", ct);
                if (errCode == "unsupported_version")
                {
                    break;
                }
                continue;
            }

            await DispatchAuthenticatedMessageAsync(session, device, env!, remoteIp, ct);
        }
    }

    private async Task DispatchAuthenticatedMessageAsync(
        ConnectedDeviceSession session,
        RegisteredDevice device,
        ProtocolEnvelope env,
        string remoteIp,
        CancellationToken ct)
    {
        var msgTime = env.Ts.HasValue && env.Ts.Value > 0
            ? DateTimeOffset.FromUnixTimeSeconds(env.Ts.Value)
            : DateTimeOffset.UtcNow;

        switch (env.T)
        {
            case "heartbeat":
                await HandleHeartbeatAsync(device, env.D, remoteIp, ct);
                break;

            case "telemetry":
                await HandleTelemetryAsync(session, device, env.Seq, msgTime, env.D, remoteIp, ct);
                break;

            case "event":
                await HandleEventAsync(session, device, env.Seq, msgTime, env.D, remoteIp, ct);
                break;

            case "ack":
                HandleAck(session, env.D);
                break;

            default:
                await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, $"tipo_no_soportado:{env.T}", ct);
                await SendErrorAsync(session, "unsupported_type", $"Tipo '{env.T}' no permitido.", ct);
                break;
        }
    }

    private async Task HandleHeartbeatAsync(
        RegisteredDevice device,
        JsonElement d,
        string remoteIp,
        CancellationToken ct)
    {
        int? batteryPct = null;
        if (d.TryGetProperty("battery_pct", out var batProp) && batProp.ValueKind != JsonValueKind.Null)
        {
            if (batProp.ValueKind != JsonValueKind.Number || !batProp.TryGetInt32(out var b) || b is < 0 or > 100)
            {
                await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "heartbeat_bateria_invalida", ct);
                return;
            }
            batteryPct = b;
        }

        if (d.TryGetProperty("uptime_s", out var upProp) && upProp.ValueKind != JsonValueKind.Null)
        {
            if (upProp.ValueKind != JsonValueKind.Number || !upProp.TryGetInt64(out var up) || up < 0)
            {
                await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "heartbeat_uptime_invalido", ct);
                return;
            }
        }

        await _repository.RecordHeartbeatAsync(
            device.Id,
            device.CunaId,
            device.Tipo,
            batteryPct,
            device.FwVersion,
            DateTimeOffset.UtcNow,
            ct);
    }

    private async Task HandleTelemetryAsync(
        ConnectedDeviceSession session,
        RegisteredDevice device,
        long seq,
        DateTimeOffset ts,
        JsonElement d,
        string remoteIp,
        CancellationToken ct)
    {
        if (seq <= 0)
        {
            await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "telemetry_seq_invalido", ct);
            await SendErrorAsync(session, "invalid_seq", "Telemetría requiere seq > 0.", ct);
            return;
        }

        // Regla 4 de AGENTS.md: ignorar cualquier family_id o cuna_id del mensaje; usar device.CunaId de la BD
        if (!TcpProtocolParser.TryValidateTelemetry(
                device.Tipo,
                device.CunaId,
                device.Id,
                seq,
                ts,
                d,
                out var telemetryRecord,
                out var errorMsg))
        {
            await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "telemetria_fuera_de_rango", ct);
            await SendErrorAsync(session, "invalid_telemetry", errorMsg ?? "Telemetría inválida.", ct);
            return;
        }

        // Deduplicación por número de secuencia (búfer offline tras reconexión)
        if (await _repository.TryAdvanceSeqAsync(device.Id, seq, ct))
        {
            await _repository.SaveTelemetryAsync(telemetryRecord!, ct);
        }

        await SendAckAsync(session, seq.ToString(), true, ct);
    }

    private async Task HandleEventAsync(
        ConnectedDeviceSession session,
        RegisteredDevice device,
        long seq,
        DateTimeOffset ts,
        JsonElement d,
        string remoteIp,
        CancellationToken ct)
    {
        if (seq <= 0)
        {
            await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "event_seq_invalido", ct);
            await SendErrorAsync(session, "invalid_seq", "Evento requiere seq > 0.", ct);
            return;
        }

        if (!TcpProtocolParser.TryValidateEvent(
                device.CunaId,
                device.Id,
                ts,
                d,
                out var alertRecord,
                out var toyUsageRecord,
                out var errorMsg))
        {
            await RecordValidationFailureAsync(device.FamilyId, device.Id, remoteIp, "evento_invalido", ct);
            await SendErrorAsync(session, "invalid_event", errorMsg ?? "Evento inválido.", ct);
            return;
        }

        if (await _repository.TryAdvanceSeqAsync(device.Id, seq, ct))
        {
            if (alertRecord is not null)
            {
                var shouldSave = DeviceAlertInterceptor is null ||
                                 await DeviceAlertInterceptor(device, alertRecord, ct);
                if (shouldSave)
                {
                    await _repository.SaveEventAlertAsync(alertRecord, ct);
                }
            }
            else if (toyUsageRecord is not null)
            {
                await _repository.SaveToyUsageAsync(toyUsageRecord, ct);
            }
        }

        await SendAckAsync(session, seq.ToString(), true, ct);
    }

    private static void HandleAck(ConnectedDeviceSession session, JsonElement d)
    {
        if (!d.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var id = idProp.GetString() ?? "";
        var ok = !d.TryGetProperty("ok", out var okProp) || okProp.ValueKind == JsonValueKind.True;
        session.CompletePendingAck(id, ok);
    }

    private static object BuildEnvelope(ConnectedDeviceSession session, string type, DateTimeOffset ts, object payload)
        => new
        {
            v = 1,
            t = type,
            seq = session.NextOutboundSeq(),
            ts = ts.ToUnixTimeSeconds(),
            d = payload
        };

    private static Task SendAckAsync(ConnectedDeviceSession session, string id, bool ok, CancellationToken ct)
        => session.TrySendJsonLineAsync(BuildEnvelope(session, "ack", DateTimeOffset.UtcNow, new { id, ok }), ct);

    private static Task SendErrorAsync(ConnectedDeviceSession session, string code, string msg, CancellationToken ct)
        => session.TrySendJsonLineAsync(BuildEnvelope(session, "error", DateTimeOffset.UtcNow, new { code, msg }), ct);

    private Task RecordValidationFailureAsync(
        Guid? familyId,
        Guid? deviceRowId,
        string remoteIp,
        string reason,
        CancellationToken ct)
    {
        return _repository.LogSecurityEventAsync(new SecurityAuditEntry(
            familyId,
            null,
            deviceRowId,
            "tcp_mensaje_rechazado",
            remoteIp,
            reason,
            DateTimeOffset.UtcNow), ct);
    }

    private async Task<bool> HandleHelloFailureAsync(
        ConnectedDeviceSession session,
        string remoteIp,
        int failedHelloCountOnConnection,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var shouldBlock = RegisterIpHelloFailure(remoteIp) >= _options.MaxFailedHelloAttempts ||
                          failedHelloCountOnConnection >= _options.MaxFailedHelloAttempts;
        if (shouldBlock)
        {
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                null,
                null,
                "hello_bloqueo_ip",
                remoteIp,
                "3_intentos_hello_fallidos",
                DateTimeOffset.UtcNow), ct);
        }

        await SendErrorAsync(session, errorCode, errorMessage, ct);
        return shouldBlock;
    }

    private bool IsIpTemporarilyBlocked(string ip)
    {
        if (!_ipFailures.TryGetValue(ip, out var state))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.BlockedUntil is { } until)
        {
            if (now < until)
            {
                return true;
            }

            _ipFailures.TryRemove(new KeyValuePair<string, IpFailureState>(ip, state));
        }

        return false;
    }

    private int RegisterIpHelloFailure(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        if (_ipFailures.Count > 128)
        {
            PruneExpiredIpFailures(now);
        }

        var updated = _ipFailures.AddOrUpdate(
            ip,
            _ => new IpFailureState(1, _options.MaxFailedHelloAttempts <= 1 ? now.Add(_options.IpBlockDuration) : null, now),
            (_, prev) =>
            {
                if (prev.BlockedUntil is { } until && now >= until)
                {
                    return new IpFailureState(1, null, now);
                }

                var nextCount = prev.Count + 1;
                var blockedUntil = nextCount >= _options.MaxFailedHelloAttempts
                    ? now.Add(_options.IpBlockDuration)
                    : prev.BlockedUntil;
                return new IpFailureState(nextCount, blockedUntil, now);
            });

        return updated.Count;
    }

    private void PruneExpiredIpFailures(DateTimeOffset now)
    {
        foreach (var entry in _ipFailures)
        {
            var expiredBlock = entry.Value.BlockedUntil is { } until && now >= until;
            var staleUnblocked = entry.Value.BlockedUntil is null && now - entry.Value.LastFailureUtc >= _options.IpBlockDuration;
            if (expiredBlock || staleUnblocked)
            {
                _ipFailures.TryRemove(entry);
            }
        }
    }

    private void ClearIpFailures(string ip)
    {
        _ipFailures.TryRemove(ip, out _);
    }

    private static DeviceTcpServerOptions BuildOptionsFromConfig(IConfiguration? config)
    {
        var isTestHost = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);

        var opts = new DeviceTcpServerOptions
        {
            Port = isTestHost ? 0 : 9000
        };

        if (config is null)
        {
            return opts;
        }

        if (int.TryParse(config["TCP_PORT"], out var port) && port is >= 0 and <= 65535)
        {
            opts.Port = port;
        }

        var bindAddr = config["TCP_BIND_ADDRESS"];
        if (!string.IsNullOrWhiteSpace(bindAddr) && IPAddress.TryParse(bindAddr, out var ip))
        {
            opts.BindAddress = ip;
        }

        return opts;
    }

    private sealed record IpFailureState(int Count, DateTimeOffset? BlockedUntil, DateTimeOffset LastFailureUtc);

    private sealed class ConnectedDeviceSession
    {
        private readonly TcpClient _client;
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingAcks = new(StringComparer.Ordinal);
        private long _outboundSeq;

        public ConnectedDeviceSession(TcpClient client, Stream stream)
        {
            _client = client;
            _stream = stream;
        }

        public string DeviceId { get; set; } = "";

        public long NextOutboundSeq() => Interlocked.Increment(ref _outboundSeq);

        public Task<bool> RegisterPendingAck(string id)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAcks[id] = tcs;
            return tcs.Task;
        }

        public void CompletePendingAck(string id, bool ok)
        {
            if (_pendingAcks.TryRemove(id, out var tcs))
            {
                tcs.TrySetResult(ok);
            }
        }

        public void CancelPendingAck(string id)
        {
            if (_pendingAcks.TryRemove(id, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }

        /// <summary>
        /// Serializa el sobre a UTF-8 y agrega el terminador <c>\n</c> del framing de protocolo TCP.
        /// </summary>
        public async Task<bool> TrySendJsonLineAsync(object envelope, CancellationToken ct)
        {
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var buffer = new byte[utf8Bytes.Length + 1];
            Buffer.BlockCopy(utf8Bytes, 0, buffer, 0, utf8Bytes.Length);
            buffer[^1] = (byte)'\n';

            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(buffer, ct);
                await _stream.FlushAsync(ct);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Close()
        {
            foreach (var kvp in _pendingAcks)
            {
                kvp.Value.TrySetResult(false);
            }
            _pendingAcks.Clear();

            try
            {
                _client.Close();
            }
            catch
            {
                // Ignorar errores al cerrar socket
            }
        }
    }
}
