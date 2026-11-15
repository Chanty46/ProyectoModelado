using System.Collections.Concurrent;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Tcp;
using CunaServer.Telegram;

namespace CunaServer.Services;

public sealed class CribLiveControlOptions
{
    public TimeSpan RemovalGraceWindow { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan AttentionModeDuration { get; set; } = TimeSpan.FromSeconds(900);
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan WatchdogPollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class CribLiveControlService
{
    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly DeviceTcpServer _tcpServer;
    private readonly CribLiveControlOptions _options;
    private readonly ILogger<CribLiveControlService> _logger;
    private readonly TelegramBotService? _telegramService;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _removalGraceWindows = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _cribModeLocks = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _cribTcpDispatchLocks = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cribTcpDispatchCts = new();
    private readonly ConcurrentDictionary<Guid, long> _cribModeEpochs = new();

    public CribLiveControlService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        DeviceTcpServer tcpServer,
        ILogger<CribLiveControlService> logger,
        CribLiveControlOptions? options = null,
        TelegramBotService? telegramService = null)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _tcpServer = tcpServer;
        _logger = logger;
        _options = options ?? new CribLiveControlOptions();
        _telegramService = telegramService;
        _tcpServer.DeviceAlertInterceptor = ProcessIncomingDeviceEventAsync;
    }

    public CribLiveControlOptions Options => _options;

    public bool IsInRemovalGraceWindow(Guid cunaId)
        => _removalGraceWindows.ContainsKey(cunaId);

    public async Task<ServiceResult<LiveCribStatusResponse>> GetLiveCribStatusAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, crib, forbidden) = await VerifyFamilyCribMembershipAsync(
            identity,
            cunaId,
            "consultar_cuna_en_vivo",
            requirePrincipal: false,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<LiveCribStatusResponse>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        // Si el Modo Atención ya expiró, regresarlo automáticamente a Monitoreo (RF05)
        if (crib.ModoOperacion == "atencion" &&
            crib.ModoAtencionExpiraEn.HasValue &&
            DateTimeOffset.UtcNow >= crib.ModoAtencionExpiraEn.Value)
        {
            crib = await ApplyModeTransitionAsync(crib, "monitoreo", null, null, "temporizador_900s", ct) ?? crib;
        }

        var configTask = _deviceRepo.GetCribConfigAsync(cunaId, membership.FamilyId, ct);
        var telemetryTask = _deviceRepo.ListTelemetryForCribAsync(cunaId, ct);
        var alertsTask = _deviceRepo.ListAlertsForCribAsync(cunaId, ct);
        var devicesTask = _deviceRepo.ListDevicesByCribAsync(cunaId, ct);

        await Task.WhenAll(configTask, telemetryTask, alertsTask, devicesTask);

        var config = await configTask;
        var telemetryList = await telemetryTask;
        var recentTelemetry = telemetryList.TakeLast(20).ToList();
        var latestTelemetry = ConsolidateLatestTelemetry(telemetryList);

        var alertsList = await alertsTask;
        var recentAlerts = alertsList.TakeLast(20).Reverse().ToList();
        var devices = await devicesTask;

        return ServiceResult<LiveCribStatusResponse>.Ok(new LiveCribStatusResponse(
            crib,
            config,
            latestTelemetry,
            recentTelemetry,
            recentAlerts,
            devices,
            IsInRemovalGraceWindow(cunaId)));
    }

    public async Task<ServiceResult<CribConfig>> GetCribThresholdsAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, crib, forbidden) = await VerifyFamilyCribMembershipAsync(
            identity,
            cunaId,
            "consultar_umbrales_cuna",
            requirePrincipal: false,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<CribConfig>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var cfg = await _deviceRepo.GetCribConfigAsync(cunaId, membership.FamilyId, ct);
        return ServiceResult<CribConfig>.Ok(cfg);
    }

    public async Task<ServiceResult<CribRecord>> SetCribModeAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        SetCribModeRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<CribRecord>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        var (membership, crib, forbidden) = await VerifyFamilyCribMembershipAsync(
            identity,
            cunaId,
            "cambiar_modo_operacion_rf05",
            requirePrincipal: false,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<CribRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var targetMode = (request.Modo ?? "").Trim().ToLowerInvariant();
        if (targetMode is not ("monitoreo" or "atencion"))
        {
            return ServiceResult<CribRecord>.BadRequest("El modo operativo debe ser 'monitoreo' o 'atencion' (RF05).");
        }

        var updated = await ApplyModeTransitionAsync(
            crib,
            targetMode,
            identity.UserId,
            request.PerfilId,
            source: "web",
            ct);

        return updated is null
            ? ServiceResult<CribRecord>.NotFound("Cuna no encontrada.")
            : ServiceResult<CribRecord>.Ok(updated);
    }

    public async Task<ServiceResult<CribRecord>> SetCribLightAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        SetCribLightRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<CribRecord>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        var (membership, crib, forbidden) = await VerifyFamilyCribMembershipAsync(
            identity,
            cunaId,
            "regular_luz_nocturna_rf09",
            requirePrincipal: false,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<CribRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        if (request.Nivel is < 0 or > 100)
        {
            return ServiceResult<CribRecord>.BadRequest("El nivel de iluminación nocturna debe estar entre 0 y 100 % (RF09).");
        }

        var effectiveLevel = request.Encendida ? (request.Nivel == 0 ? 50 : request.Nivel) : 0;
        var updated = await _deviceRepo.UpdateCribLightAsync(cunaId, request.Encendida, effectiveLevel, ct);
        if (updated is null)
        {
            return ServiceResult<CribRecord>.NotFound("Cuna no encontrada.");
        }

        var central = await _deviceRepo.GetCentralDeviceForCribAsync(cunaId, ct);
        if (central is not null)
        {
            await _tcpServer.SendCommandAsync(
                central.DeviceId,
                "light_set",
                new
                {
                    on = request.Encendida,
                    level = effectiveLevel,
                    ramp_ms = 1500,
                    pwm_hz = 1000
                },
                requireAck: false,
                ct: ct);
        }

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "luz_nocturna_rf09",
            JsonSerializer.Serialize(new { cuna_id = cunaId, encendida = request.Encendida, nivel = effectiveLevel }),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<CribRecord>.Ok(updated);
    }

    public async Task<ServiceResult<CribConfig>> UpdateCribThresholdsAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        UpdateCribThresholdsRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<CribConfig>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        // RF08 + RF03: Exclusivo del Usuario Principal
        var (membership, crib, forbidden) = await VerifyFamilyCribMembershipAsync(
            identity,
            cunaId,
            "ajustar_umbrales_rf08",
            requirePrincipal: true,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<CribConfig>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        if (request.TempMin is < 10.0m or > 30.0m ||
            request.TempMax is < 15.0m or > 38.0m ||
            request.TempMax <= request.TempMin)
        {
            return ServiceResult<CribConfig>.BadRequest(
                "Rango térmico inválido (temp_min 10–30 °C, temp_max 15–38 °C y temp_max > temp_min).");
        }

        if (request.HumMin is < 10.0m or > 80.0m ||
            request.HumMax is < 20.0m or > 95.0m ||
            request.HumMax <= request.HumMin)
        {
            return ServiceResult<CribConfig>.BadRequest(
                "Rango de humedad inválido (hum_min 10–80 %, hum_max 20–95 % y hum_max > hum_min).");
        }

        if (request.MovimientoUmbral is <= 0m or > 10.0m)
        {
            return ServiceResult<CribConfig>.BadRequest("El umbral de movimiento debe estar entre 0.001 y 10.000 kg/min (RF29).");
        }

        if (request.RuidoMasaPiso is < 0m or > 1.0m)
        {
            return ServiceResult<CribConfig>.BadRequest("El piso de ruido de masa debe estar entre 0.000 y 1.000 kg.");
        }

        if (request.EscalaMasa is < 0.05m or > 1.0m)
        {
            return ServiceResult<CribConfig>.BadRequest("La escala de masa (mass_scale) debe estar entre 0.05 y 1.00 (D14).");
        }

        var updatedCfg = await _deviceRepo.UpdateCribThresholdsAsync(cunaId, membership.FamilyId, request, ct);

        // Enviar nuevos umbrales por TCP a la Unidad Central y al Colchón si están conectados (RF08, PROTOCOL.md: comando crítico con ACK)
        var devices = await _deviceRepo.ListActiveDevicesForCribAsync(cunaId, ct);
        var targetDeviceIds = devices
            .Where(d => d.Tipo is DeviceTypes.Central or DeviceTypes.Colchon)
            .Select(d => d.DeviceId)
            .ToList();
        if (targetDeviceIds.Count > 0)
        {
            var thresholdsPayload = new
            {
                mass_scale = updatedCfg.EscalaMasa,
                temp_min = updatedCfg.TempMin,
                temp_max = updatedCfg.TempMax,
                hum_min = updatedCfg.HumMin,
                hum_max = updatedCfg.HumMax,
                movement_threshold = updatedCfg.MovimientoUmbral,
                movement_alert_enabled = updatedCfg.MovimientoAlertaActiva,
                mass_noise_floor = updatedCfg.RuidoMasaPiso
            };
            _ = Task.Run(async () =>
            {
                foreach (var targetDeviceId in targetDeviceIds)
                {
                    try
                    {
                        await _tcpServer.SendCommandAsync(
                            targetDeviceId,
                            "thresholds_set",
                            thresholdsPayload,
                            requireAck: true,
                            ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error enviando thresholds_set a {DeviceId}", targetDeviceId);
                    }
                }
            }, CancellationToken.None);
        }

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "ajuste_umbrales_rf08",
            JsonSerializer.Serialize(new
            {
                cuna_id = cunaId,
                temp_min = updatedCfg.TempMin,
                temp_max = updatedCfg.TempMax,
                hum_min = updatedCfg.HumMin,
                hum_max = updatedCfg.HumMax,
                movement_threshold = updatedCfg.MovimientoUmbral,
                movement_alert_enabled = updatedCfg.MovimientoAlertaActiva,
                mass_scale = updatedCfg.EscalaMasa
            }),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<CribConfig>.Ok(updatedCfg);
    }

    /// <summary>
    /// Procesa eventos entrantes por TCP de los nodos Pico W según la máquina de estados en el servidor (D10).
    /// Devuelve true si la alerta debe persistirse en eventos_alertas.
    /// </summary>
    public async Task<bool> ProcessIncomingDeviceEventAsync(
        RegisteredDevice device,
        EventAlertRecord alertRecord,
        CancellationToken ct)
    {
        var crib = await _deviceRepo.GetCribByIdAsync(device.CunaId, ct);
        if (crib is null)
        {
            return true;
        }

        switch (alertRecord.Tipo)
        {
            case "button_attention":
            {
                // RF05: El botón táctil de la Unidad Central conmuta Modo Atención (900 s) o silencia alarma
                var isCurrentlyAttention =
                    crib.ModoOperacion == "atencion" &&
                    (!crib.ModoAtencionExpiraEn.HasValue || DateTimeOffset.UtcNow < crib.ModoAtencionExpiraEn.Value);

                var nextMode = isCurrentlyAttention ? "monitoreo" : "atencion";
                await ApplyModeTransitionAsync(crib, nextMode, null, null, source: "boton_tactil_central", ct);
                return true;
            }

            case "removal":
            {
                // RF04 + RF05: Si Modo Atención está activo, se ignora el disparo de alarma
                var isAttentionActive =
                    crib.ModoOperacion == "atencion" &&
                    (!crib.ModoAtencionExpiraEn.HasValue || DateTimeOffset.UtcNow < crib.ModoAtencionExpiraEn.Value);

                if (isAttentionActive)
                {
                    return false;
                }

                // Abrir ventana de gracia de 5 segundos antes de ordenar la alarma crítica (RF04 / D10)
                StartRemovalGraceWindow(crib.Id, device.Id);
                return true;
            }

            case "movement_high":
            {
                // RF29: Verificar si la alerta por movimiento anómalo está habilitada en umbrales
                var cfg = await _deviceRepo.GetCribConfigAsync(crib.Id, crib.FamilyId, ct);
                if (!cfg.MovimientoAlertaActiva)
                {
                    return false;
                }

                if (_telegramService is not null)
                {
                    await _telegramService.DispatchAlertForCribAsync(crib.Id, alertRecord, ct);
                }
                return true;
            }

            default:
            {
                if (_telegramService is not null && alertRecord.Tipo is "env_out_of_range" or "battery_low")
                {
                    await _telegramService.DispatchAlertForCribAsync(crib.Id, alertRecord, ct);
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Evalúa la expiración de Modo Atención (900 s, RF05) y el Watchdog de desconexión (> 60 s sin heartbeat, RF20).
    /// </summary>
    public async Task EvaluateWatchdogAndTimersAsync(DateTimeOffset now, CancellationToken ct)
    {
        var allCribs = await _deviceRepo.ListAllCribsAsync(ct);

        foreach (var crib in allCribs)
        {
            // 1. RF05: Expiración automática del Modo Atención tras 900 s
            if (crib.ModoOperacion == "atencion" &&
                crib.ModoAtencionExpiraEn.HasValue &&
                now >= crib.ModoAtencionExpiraEn.Value)
            {
                await ApplyModeTransitionAsync(crib, "monitoreo", null, null, source: "temporizador_900s", ct);
            }

            // 2. RF20: Watchdog en servidor (> 60 s sin heartbeat de la Unidad Central)
            if (crib.EstadoConexion == "en_linea" &&
                crib.UltimoHeartbeat.HasValue &&
                (now - crib.UltimoHeartbeat.Value) > _options.WatchdogTimeout)
            {
                await _deviceRepo.UpdateCribConnectionStateAsync(crib.Id, "desconectada", ct);

                var central = await _deviceRepo.GetCentralDeviceForCribAsync(crib.Id, ct);
                var watchdogAlert = new EventAlertRecord(
                    Guid.NewGuid(),
                    crib.Id,
                    central?.Id ?? Guid.Empty,
                    "watchdog_offline",
                    "critica",
                    "Alerta crítica (RF20): pérdida de conexión con la Unidad Central (> 60 s sin heartbeat).",
                    JsonSerializer.Serialize(new
                    {
                        kind = "watchdog_offline",
                        last_heartbeat = crib.UltimoHeartbeat.Value
                    }),
                    now);
                await _deviceRepo.SaveEventAlertAsync(watchdogAlert, ct);

                if (_telegramService is not null)
                {
                    await _telegramService.DispatchAlertForCribAsync(crib.Id, watchdogAlert, ct);
                }
            }
        }
    }

    private async Task<CribRecord?> ApplyModeTransitionAsync(
        CribRecord crib,
        string targetMode,
        Guid? userId,
        Guid? perfilId,
        string source,
        CancellationToken ct)
    {
        var cribLock = _cribModeLocks.GetOrAdd(crib.Id, _ => new SemaphoreSlim(1, 1));
        CribRecord? updated;
        long transitionEpoch;
        CancellationTokenSource? dispatchCts = null;

        await cribLock.WaitAsync(ct);
        try
        {
            CancelRemovalGraceWindow(crib.Id);
            transitionEpoch = _cribModeEpochs.AddOrUpdate(crib.Id, 1, (_, prev) => prev + 1);

            DateTimeOffset? expiraEn = targetMode == "atencion"
                ? DateTimeOffset.UtcNow.Add(_options.AttentionModeDuration)
                : null;

            updated = await _deviceRepo.UpdateCribModeAsync(crib.Id, targetMode, expiraEn, ct);
            if (updated is not null)
            {
                dispatchCts = ReplaceCribTcpDispatchCts(crib.Id);
            }
        }
        finally
        {
            cribLock.Release();
        }

        if (updated is null)
        {
            return null;
        }

        var centralTask = _deviceRepo.GetCentralDeviceForCribAsync(crib.Id, ct);
        var auditTask = _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            crib.FamilyId,
            userId,
            perfilId,
            $"modo_{targetMode}_rf05",
            JsonSerializer.Serialize(new { cuna_id = crib.Id, modo = targetMode, origen = source, expira_en = updated.ModoAtencionExpiraEn }),
            DateTimeOffset.UtcNow), ct);

        await Task.WhenAll(centralTask, auditTask);
        var central = await centralTask;

        if (central is not null && dispatchCts is not null)
        {
            // Serializar comandos TCP críticos por cuna en segundo plano (PROTOCOL.md: alarm_set y mode_set exigen ACK)
            var ledColor = targetMode == "atencion" ? "yellow" : "green";
            var durationSec = (int)_options.AttentionModeDuration.TotalSeconds;
            var activeDispatchCts = dispatchCts;
            _ = Task.Run(async () =>
            {
                var tcpLock = _cribTcpDispatchLocks.GetOrAdd(crib.Id, _ => new SemaphoreSlim(1, 1));
                await tcpLock.WaitAsync(CancellationToken.None);
                try
                {
                    if (!activeDispatchCts.IsCancellationRequested &&
                        _cribModeEpochs.GetValueOrDefault(crib.Id) == transitionEpoch)
                    {
                        await _tcpServer.SendCommandAsync(
                            central.DeviceId,
                            "alarm_set",
                            new { on = false },
                            requireAck: true,
                            ct: activeDispatchCts.Token);
                    }

                    if (!activeDispatchCts.IsCancellationRequested &&
                        _cribModeEpochs.GetValueOrDefault(crib.Id) == transitionEpoch)
                    {
                        await _tcpServer.SendCommandAsync(
                            central.DeviceId,
                            "mode_set",
                            new { mode = targetMode, duration_s = targetMode == "atencion" ? durationSec : 0 },
                            requireAck: true,
                            ct: activeDispatchCts.Token);
                    }

                    if (!activeDispatchCts.IsCancellationRequested &&
                        _cribModeEpochs.GetValueOrDefault(crib.Id) == transitionEpoch)
                    {
                        await _tcpServer.SendCommandAsync(
                            central.DeviceId,
                            "status_led",
                            new { color = ledColor },
                            requireAck: false,
                            ct: activeDispatchCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Transición reemplazada por un cambio de modo más reciente
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error enviando comandos TCP de transición de modo a {DeviceId}", central.DeviceId);
                }
                finally
                {
                    tcpLock.Release();
                    ReleaseCribTcpDispatchCts(crib.Id, activeDispatchCts);
                }
            }, CancellationToken.None);
        }
        else if (dispatchCts is not null)
        {
            ReleaseCribTcpDispatchCts(crib.Id, dispatchCts);
        }

        return updated;
    }

    private void StartRemovalGraceWindow(Guid cunaId, Guid triggerDeviceId)
    {
        // Si ya hay una ventana de gracia activa para esta cuna, preservar el temporizador inicial
        // para que eventos repetidos de retiro no posterguen indefinidamente la alarma (RF04).
        if (_removalGraceWindows.ContainsKey(cunaId))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        if (!_removalGraceWindows.TryAdd(cunaId, cts))
        {
            cts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.RemovalGraceWindow, cts.Token);

                var cribLock = _cribModeLocks.GetOrAdd(cunaId, _ => new SemaphoreSlim(1, 1));
                RegisteredDevice? central = null;
                EventAlertRecord? alarmAlert = null;
                CancellationTokenSource? cribDispatchCts = null;
                long alarmEpoch = 0;

                await cribLock.WaitAsync(CancellationToken.None);
                try
                {
                    if (!_removalGraceWindows.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(cunaId, cts)) ||
                        cts.IsCancellationRequested)
                    {
                        return;
                    }

                    var crib = await _deviceRepo.GetCribByIdAsync(cunaId, CancellationToken.None);
                    if (crib is null)
                    {
                        return;
                    }

                    // Verificar dentro del bloqueo que no se haya activado Modo Atención durante la ventana de gracia
                    var isAttentionActive =
                        crib.ModoOperacion == "atencion" &&
                        (!crib.ModoAtencionExpiraEn.HasValue || DateTimeOffset.UtcNow < crib.ModoAtencionExpiraEn.Value);
                    if (isAttentionActive)
                    {
                        return;
                    }

                    alarmEpoch = _cribModeEpochs.AddOrUpdate(cunaId, 1, (_, prev) => prev + 1);
                    await _deviceRepo.UpdateCribModeAsync(cunaId, "alarma", null, CancellationToken.None);
                    var dispatchCts = ReplaceCribTcpDispatchCts(cunaId);

                    alarmAlert = new EventAlertRecord(
                        Guid.NewGuid(),
                        cunaId,
                        triggerDeviceId,
                        "removal_alarm",
                        "critica",
                        "Alarma crítica (RF04): retiro imprevisto del infante sin pausa de seguridad tras 5 s.",
                        JsonSerializer.Serialize(new { kind = "removal_alarm", grace_s = _options.RemovalGraceWindow.TotalSeconds }),
                        DateTimeOffset.UtcNow);

                    cribDispatchCts = dispatchCts;
                }
                finally
                {
                    cribLock.Release();
                }

                if (alarmAlert is null || cribDispatchCts is null)
                {
                    if (cribDispatchCts is not null)
                    {
                        ReleaseCribTcpDispatchCts(cunaId, cribDispatchCts);
                    }
                    return;
                }

                try
                {
                    await _deviceRepo.SaveEventAlertAsync(alarmAlert, CancellationToken.None);

                    if (_telegramService is not null)
                    {
                        await _telegramService.DispatchAlertForCribAsync(cunaId, alarmAlert, CancellationToken.None);
                    }

                    central = await _deviceRepo.GetCentralDeviceForCribAsync(cunaId, CancellationToken.None);
                    if (central is not null)
                    {
                        var tcpLock = _cribTcpDispatchLocks.GetOrAdd(cunaId, _ => new SemaphoreSlim(1, 1));
                        await tcpLock.WaitAsync(CancellationToken.None);
                        try
                        {
                            if (!cribDispatchCts.IsCancellationRequested &&
                                _cribModeEpochs.GetValueOrDefault(cunaId) == alarmEpoch)
                            {
                                await _tcpServer.SendCommandAsync(
                                    central.DeviceId,
                                    "alarm_set",
                                    new { on = true, reason = "removal" },
                                    requireAck: true,
                                    ct: cribDispatchCts.Token);
                            }

                            if (!cribDispatchCts.IsCancellationRequested &&
                                _cribModeEpochs.GetValueOrDefault(cunaId) == alarmEpoch)
                            {
                                await _tcpServer.SendCommandAsync(
                                    central.DeviceId,
                                    "status_led",
                                    new { color = "red" },
                                    requireAck: false,
                                    ct: cribDispatchCts.Token);
                            }
                        }
                        finally
                        {
                            tcpLock.Release();
                        }
                    }
                }
                finally
                {
                    ReleaseCribTcpDispatchCts(cunaId, cribDispatchCts);
                }
            }
            catch (OperationCanceledException)
            {
                // Ventana de gracia cancelada a tiempo por activación de Modo Atención (RF05)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ventana de gracia RF04 para cuna {CunaId}", cunaId);
            }
            finally
            {
                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private CancellationTokenSource ReplaceCribTcpDispatchCts(Guid cunaId)
    {
        var next = new CancellationTokenSource();
        while (true)
        {
            if (!_cribTcpDispatchCts.TryGetValue(cunaId, out var existing))
            {
                if (_cribTcpDispatchCts.TryAdd(cunaId, next))
                {
                    return next;
                }
            }
            else if (_cribTcpDispatchCts.TryUpdate(cunaId, next, existing))
            {
                try
                {
                    existing.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // El despacho anterior ya finalizó
                }
                return next;
            }
        }
    }

    private void ReleaseCribTcpDispatchCts(Guid cunaId, CancellationTokenSource dispatchCts)
    {
        _cribTcpDispatchCts.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(cunaId, dispatchCts));
        dispatchCts.Dispose();
    }

    private void CancelRemovalGraceWindow(Guid cunaId)
    {
        if (_removalGraceWindows.TryRemove(cunaId, out var existingCts))
        {
            try
            {
                existingCts.Cancel();
            }
            catch
            {
                // Ignorar si ya fue dispuesto
            }
        }
    }

    public static TelemetryRecord? ConsolidateLatestTelemetry(IReadOnlyList<TelemetryRecord> telemetryList)
    {
        if (telemetryList.Count == 0)
        {
            return null;
        }

        var newest = telemetryList[^1];
        decimal? masaKg = null;
        decimal? indiceMovimiento = null;
        decimal? tempC = null;
        decimal? humedad = null;
        decimal? masaBiberonG = null;
        decimal? tempBiberonC = null;
        int? bateriaPct = null;

        for (var i = telemetryList.Count - 1; i >= 0; i--)
        {
            var item = telemetryList[i];
            masaKg ??= item.MasaKg;
            indiceMovimiento ??= item.IndiceMovimiento;
            tempC ??= item.TempC;
            humedad ??= item.Humedad;
            masaBiberonG ??= item.MasaBiberonG;
            tempBiberonC ??= item.TempBiberonC;
            bateriaPct ??= item.BateriaPct;

            if (masaKg.HasValue &&
                indiceMovimiento.HasValue &&
                tempC.HasValue &&
                humedad.HasValue &&
                masaBiberonG.HasValue &&
                tempBiberonC.HasValue &&
                bateriaPct.HasValue)
            {
                break;
            }
        }

        return newest with
        {
            MasaKg = masaKg,
            IndiceMovimiento = indiceMovimiento,
            TempC = tempC,
            Humedad = humedad,
            MasaBiberonG = masaBiberonG,
            TempBiberonC = tempBiberonC,
            BateriaPct = bateriaPct
        };
    }

    private Task<(UserMembership? Membership, CribRecord? Crib, string? ForbiddenMessage)> VerifyFamilyCribMembershipAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string attemptedAction,
        bool requirePrincipal,
        string? clientIp,
        CancellationToken ct)
        => FamilyAuthorizationHelper.RequireFamilyCribAccessAsync(
            _accountRepo,
            _deviceRepo,
            identity,
            cunaId,
            attemptedAction,
            requirePrincipal,
            clientIp,
            ct);
}

public sealed class CribWatchdogBackgroundService : BackgroundService
{
    private readonly CribLiveControlService _liveService;
    private readonly ILogger<CribWatchdogBackgroundService> _logger;

    public CribWatchdogBackgroundService(
        CribLiveControlService liveService,
        ILogger<CribWatchdogBackgroundService> logger)
    {
        _liveService = liveService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_liveService.Options.WatchdogPollInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _liveService.EvaluateWatchdogAndTimersAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ciclo de evaluación del Watchdog RF20.");
            }
        }
    }
}
