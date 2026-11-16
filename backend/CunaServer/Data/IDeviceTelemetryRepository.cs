namespace CunaServer.Data;

/// <summary>
/// Contrato de acceso a datos para dispositivos IoT, telemetría, alertas, emparejamiento,
/// cunas en vivo, bitácora del cuidador, bebés y sesiones de alimentación y sueño.
/// </summary>
public interface IDeviceTelemetryRepository
{
    #region Autenticación de dispositivos, anti-replay y telemetría (M3)

    /// <summary>
    /// Obtiene un dispositivo registrado por su <c>device_id</c>, deduciendo <c>family_id</c> desde la cuna asociada.
    /// </summary>
    Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct);

    /// <summary>
    /// Obtiene el dispositivo central activo vinculado a una cuna.
    /// </summary>
    Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Obtiene la configuración vigente de escala de masa y umbrales de alerta de una cuna perteneciente a la familia.
    /// </summary>
    Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct);

    /// <summary>
    /// Registra el heartbeat del dispositivo y, si es el nodo central, marca la cuna como en línea.
    /// </summary>
    Task RecordHeartbeatAsync(Guid deviceRowId, Guid cunaId, string deviceType, int? batteryPct, string? fwVersion, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Avanza de forma atómica y monotónica el contador <c>ultimo_seq</c> para prevenir ataques de repetición (anti-replay).
    /// </summary>
    Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct);

    /// <summary>
    /// Persiste una lectura de telemetría ignorando duplicados idempotentes por dispositivo/secuencia.
    /// </summary>
    Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct);

    /// <summary>
    /// Persiste una alerta o evento generado por los sensores o reglas de umbral.
    /// </summary>
    Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct);

    /// <summary>
    /// Persiste un registro de uso del juguete o móvil musical.
    /// </summary>
    Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct);

    /// <summary>
    /// Registra un evento en la bitácora de seguridad sin almacenar tokens ni datos sensibles del bebé.
    /// </summary>
    Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct);

    /// <summary>
    /// Lista las lecturas recientes de telemetría de una cuna ordenadas cronológicamente.
    /// </summary>
    Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Lista las alertas recientes de una cuna ordenadas cronológicamente.
    /// </summary>
    Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Lista el historial reciente de uso de juguetes de una cuna ordenado cronológicamente.
    /// </summary>
    Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct);

    #endregion

    #region Emparejamiento, cunas y perfil del bebé (M4: RF24–RF28)

    /// <summary>
    /// Obtiene un dispositivo con estado de reclamo para verificar hash de código de emparejamiento y ventana de validez.
    /// </summary>
    Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct);

    /// <summary>
    /// Incrementa el contador de intentos fallidos de reclamo hasta el límite de 5.
    /// </summary>
    Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct);

    /// <summary>
    /// Crea una cuna nueva junto con sus umbrales ambientales y de movimiento predeterminados.
    /// </summary>
    Task<CribRecord> CreateCribWithDefaultsAsync(Guid familyId, string nombre, CancellationToken ct);

    /// <summary>
    /// Obtiene una cuna por su identificador.
    /// </summary>
    Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Lista todas las cunas asociadas a una familia.
    /// </summary>
    Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Renombra una cuna verificando pertenencia a la familia.
    /// </summary>
    Task<CribRecord?> RenameCribAsync(Guid familyId, Guid cunaId, string newName, CancellationToken ct);

    /// <summary>
    /// Vincula un dispositivo reclamado a una cuna y guarda el hash SHA-256 de su token definitivo.
    /// </summary>
    Task<ClaimableDeviceRecord> BindClaimedDeviceAsync(
        Guid deviceRowId,
        Guid cunaId,
        Guid familyId,
        string hashToken,
        CancellationToken ct);

    /// <summary>
    /// Lista los dispositivos vinculados a una cuna.
    /// </summary>
    Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Desvincula una cuna de la familia, revoca todos sus dispositivos y conserva el perfil e historial del bebé (RF27).
    /// </summary>
    Task<IReadOnlyList<string>> UnlinkCribAndRevokeDevicesAsync(Guid familyId, Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Lista los bebés registrados en una familia.
    /// </summary>
    Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>
    /// Crea el perfil de un bebé asociado a la familia y a una cuna.
    /// </summary>
    Task<BabyRecord> CreateBabyAsync(Guid familyId, Guid cunaId, string nombre, DateOnly fechaNacimiento, string? notas, CancellationToken ct);

    /// <summary>
    /// Actualiza los datos de un bebé verificando que pertenezca a la familia indicada.
    /// </summary>
    Task<BabyRecord?> UpdateBabyAsync(Guid familyId, Guid bebeId, Guid cunaId, string nombre, DateOnly fechaNacimiento, string? notas, CancellationToken ct);

    #endregion

    #region Cuna en vivo y bitácora del cuidador (M5: RF04, RF05, RF07, RF08, RF09, RF20, RF29)

    /// <summary>
    /// Lista los dispositivos activos (no revocados ni sin dueño) asociados a una cuna.
    /// </summary>
    Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct);

    /// <summary>
    /// Actualiza el modo de operación de una cuna y su expiración temporal de atención.
    /// </summary>
    Task<CribRecord?> UpdateCribModeAsync(Guid cunaId, string modoOperacion, DateTimeOffset? modoAtencionExpiraEn, CancellationToken ct);

    /// <summary>
    /// Actualiza el estado y nivel de la luz nocturna de una cuna.
    /// </summary>
    Task<CribRecord?> UpdateCribLightAsync(Guid cunaId, bool luzEncendida, int luzNivel, CancellationToken ct);

    /// <summary>
    /// Actualiza la escala de masa y los umbrales de monitoreo de una cuna perteneciente a la familia.
    /// </summary>
    Task<CribConfig> UpdateCribThresholdsAsync(Guid cunaId, Guid familyId, UpdateCribThresholdsRequest request, CancellationToken ct);

    /// <summary>
    /// Actualiza el estado de conexión de una cuna y propaga <c>desconectado</c> a sus dispositivos si aplica.
    /// </summary>
    Task<CribRecord?> UpdateCribConnectionStateAsync(Guid cunaId, string estadoConexion, CancellationToken ct);

    /// <summary>
    /// Lista todas las cunas registradas para evaluación del watchdog de conexión.
    /// </summary>
    Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct);

    /// <summary>
    /// Registra una acción del cuidador en la bitácora familiar.
    /// </summary>
    Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct);

    /// <summary>
    /// Lista el historial de acciones del cuidador de una familia.
    /// </summary>
    Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct);

    #endregion

    #region Alimentación, sueño y catálogo de canciones (M7: RF12–RF17, RF30–RF37)

    /// <summary>
    /// Obtiene el perfil de un bebé por su identificador.
    /// </summary>
    Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct);

    /// <summary>
    /// Inicia una sesión de alimentación registrando masa inicial, temperatura y bandera de alerta térmica.
    /// </summary>
    Task<FeedingSessionRecord> CreateFeedingSessionAsync(
        Guid bebeId,
        Guid? perfilId,
        decimal masaInicialG,
        decimal? tempC,
        bool alertaTemperatura,
        DateTimeOffset inicio,
        CancellationToken ct);

    /// <summary>
    /// Obtiene una sesión de alimentación por su identificador.
    /// </summary>
    Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Finaliza una sesión de alimentación registrando masa final y volumen consumido en mililitros.
    /// </summary>
    Task<FeedingSessionRecord?> CompleteFeedingSessionAsync(
        Guid sessionId,
        decimal masaFinalG,
        decimal mlConsumidos,
        DateTimeOffset fin,
        CancellationToken ct);

    /// <summary>
    /// Lista las sesiones de alimentación de un bebé ordenadas de más reciente a más antigua.
    /// </summary>
    Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct);

    /// <summary>
    /// Registra una sesión de sueño o siesta de un bebé.
    /// </summary>
    Task<SleepSessionRecord> CreateSleepSessionAsync(
        Guid bebeId,
        Guid cunaId,
        string tipo,
        DateTimeOffset inicio,
        DateTimeOffset? fin,
        decimal? movimientoPromedio,
        int interrupciones,
        CancellationToken ct);

    /// <summary>
    /// Lista las sesiones de sueño de un bebé ordenadas de más reciente a más antigua.
    /// </summary>
    Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct);

    /// <summary>
    /// Lista el catálogo de canciones de cuna disponibles para el módulo móvil/juguete.
    /// </summary>
    Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct);

    #endregion
}
