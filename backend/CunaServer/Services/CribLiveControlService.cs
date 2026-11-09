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
            crib = await _deviceRepo.UpdateCribModeAsync(cunaId, "monitoreo", null, ct) ?? crib;
        }

        var config = await _deviceRepo.GetCribConfigAsync(cunaId, membership.FamilyId, ct);
        var telemetryList = await _deviceRepo.ListTelemetryForCribAsync(cunaId, ct);
        var recentTelemetry = telemetryList.TakeLast(20).ToList();
        var latestTelemetry = recentTelemetry.LastOrDefault();

        var alertsList = await _deviceRepo.ListAlertsForCribAsync(cunaId, ct);
        var recentAlerts = alertsList.TakeLast(20).Reverse().ToList();
        var devices = await _deviceRepo.ListDevicesByCribAsync(cunaId, ct);

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
        SetCribModeRequest request,
        string? clientIp,
        CancellationToken ct)
    {
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
        SetCribLightRequest request,
        string? clientIp,
        CancellationToken ct)
    {
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
        UpdateCribThresholdsRequest request,
        string? clientIp,
        CancellationToken ct)
    {
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

        if (request.EscalaMasa is <= 0m or > 1.0m)
        {
            return ServiceResult<CribConfig>.BadRequest("La escala de masa (mass_scale) debe estar entre 0.05 y 1.00 (D14).");
        }

        var updatedCfg = await _deviceRepo.UpdateCribThresholdsAsync(cunaId, membership.FamilyId, request, ct);

        // Enviar nuevos umbrales por TCP a la Unidad Central y al Colchón si están conectados (RF08)
        var devices = await _deviceRepo.ListActiveDevicesForCribAsync(cunaId, ct);
        foreach (var dev in devices.Where(d => d.Tipo is DeviceTypes.Central or DeviceTypes.Colchon))
        {
            await _tcpServer.SendCommandAsync(
                dev.DeviceId,
                "thresholds_set",
                new
                {
                    mass_scale = updatedCfg.EscalaMasa,
                    temp_min = updatedCfg.TempMin,
                    temp_max = updatedCfg.TempMax,
                    hum_min = updatedCfg.HumMin,
                    hum_max = updatedCfg.HumMax,
                    movement_threshold = updatedCfg.MovimientoUmbral,
                    movement_alert_enabled = updatedCfg.MovimientoAlertaActiva,
                    mass_noise_floor = updatedCfg.RuidoMasaPiso
                },
                requireAck: false,
                ct: ct);
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
        CancelRemovalGraceWindow(crib.Id);

        DateTimeOffset? expiraEn = targetMode == "atencion"
            ? DateTimeOffset.UtcNow.Add(_options.AttentionModeDuration)
            : null;

        var updated = await _deviceRepo.UpdateCribModeAsync(crib.Id, targetMode, expiraEn, ct);

        var central = await _deviceRepo.GetCentralDeviceForCribAsync(crib.Id, ct);
        if (central is not null)
        {
            // Silenciar alarma activa si la hubiera y actualizar modo y LED RGB (RF05 / IU-03)
            var ledColor = targetMode == "atencion" ? "yellow" : "green";
            var durationSec = (int)_options.AttentionModeDuration.TotalSeconds;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _tcpServer.SendCommandAsync(
                        central.DeviceId,
                        "alarm_set",
                        new { on = false },
                        requireAck: false,
                        ct: CancellationToken.None);

                    await _tcpServer.SendCommandAsync(
                        central.DeviceId,
                        "mode_set",
                        new { mode = targetMode, duration_s = targetMode == "atencion" ? durationSec : 0 },
                        requireAck: false,
                        ct: CancellationToken.None);

                    await _tcpServer.SendCommandAsync(
                        central.DeviceId,
                        "status_led",
                        new { color = ledColor },
                        requireAck: false,
                        ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error enviando comandos TCP de transición de modo a {DeviceId}", central.DeviceId);
                }
            }, CancellationToken.None);
        }

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            crib.FamilyId,
            userId,
            perfilId,
            $"modo_{targetMode}_rf05",
            JsonSerializer.Serialize(new { cuna_id = crib.Id, modo = targetMode, origen = source, expira_en = expiraEn }),
            DateTimeOffset.UtcNow), ct);

        return updated;
    }

    private void StartRemovalGraceWindow(Guid cunaId, Guid triggerDeviceId)
    {
        CancelRemovalGraceWindow(cunaId);

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

                if (!_removalGraceWindows.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(cunaId, cts)))
                {
                    return;
                }

                var crib = await _deviceRepo.GetCribByIdAsync(cunaId, CancellationToken.None);
                if (crib is null)
                {
                    return;
                }

                // Verificar una vez más que no se haya activado Modo Atención durante la ventana de gracia
                var isAttentionActive =
                    crib.ModoOperacion == "atencion" &&
                    (!crib.ModoAtencionExpiraEn.HasValue || DateTimeOffset.UtcNow < crib.ModoAtencionExpiraEn.Value);
                if (isAttentionActive)
                {
                    return;
                }

                await _deviceRepo.UpdateCribModeAsync(cunaId, "alarma", null, CancellationToken.None);

                var alarmAlert = new EventAlertRecord(
                    Guid.NewGuid(),
                    cunaId,
                    triggerDeviceId,
                    "removal_alarm",
                    "critica",
                    "Alarma crítica (RF04): retiro imprevisto del infante sin pausa de seguridad tras 5 s.",
                    JsonSerializer.Serialize(new { kind = "removal_alarm", grace_s = _options.RemovalGraceWindow.TotalSeconds }),
                    DateTimeOffset.UtcNow);
                await _deviceRepo.SaveEventAlertAsync(alarmAlert, CancellationToken.None);

                if (_telegramService is not null)
                {
                    await _telegramService.DispatchAlertForCribAsync(cunaId, alarmAlert, CancellationToken.None);
                }

                var central = await _deviceRepo.GetCentralDeviceForCribAsync(cunaId, CancellationToken.None);
                if (central is not null)
                {
                    await _tcpServer.SendCommandAsync(
                        central.DeviceId,
                        "alarm_set",
                        new { on = true, reason = "removal" },
                        requireAck: true,
                        ct: CancellationToken.None);

                    await _tcpServer.SendCommandAsync(
                        central.DeviceId,
                        "status_led",
                        new { color = "red" },
                        requireAck: false,
                        ct: CancellationToken.None);
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

    private async Task<(UserMembership? Membership, CribRecord? Crib, string? ForbiddenMessage)> VerifyFamilyCribMembershipAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string attemptedAction,
        bool requirePrincipal,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"sin_familia:{attemptedAction}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: el usuario no pertenece a una familia activa.");
        }

        if (requirePrincipal && !string.Equals(membership.Rol, UserRoles.Principal, StringComparison.Ordinal))
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"rol_secundario:{attemptedAction}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado (RF03): solo el Usuario Principal puede modificar umbrales.");
        }

        var crib = await _deviceRepo.GetCribByIdAsync(cunaId, ct);
        if (crib is null || crib.FamilyId != membership.FamilyId)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"cuna_ajena:{attemptedAction}:{cunaId}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: la cuna solicitada no pertenece a tu familia.");
        }

        return (membership, crib, null);
    }
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
