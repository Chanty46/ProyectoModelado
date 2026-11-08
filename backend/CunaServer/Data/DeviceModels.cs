using System.Text.Json;
using System.Text.Json.Serialization;

namespace CunaServer.Data;

public static class DeviceTypes
{
    public const string Central = "central";
    public const string Colchon = "colchon";
    public const string Movil = "movil";
    public const string Biberon = "biberon";
    public const string Juguete = "juguete";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        Central, Colchon, Movil, Biberon, Juguete
    };
}

public sealed record RegisteredDevice(
    Guid Id,
    string DeviceId,
    Guid CunaId,
    Guid FamilyId,
    string Tipo,
    string HashToken,
    string Estado,
    int? BateriaPct,
    long UltimoSeq,
    DateTimeOffset? UltimoHeartbeat,
    string? FwVersion);

public sealed record ClaimableDeviceRecord(
    Guid Id,
    string DeviceId,
    Guid? CunaId,
    Guid? FamilyId,
    string Tipo,
    string? HashReclamo,
    DateTimeOffset? ReclamoExpiraEn,
    bool ReclamoUsado,
    int IntentosReclamo,
    string? HashToken,
    string Estado,
    int? BateriaPct,
    string? FwVersion);

public sealed record CribRecord(
    Guid Id,
    Guid FamilyId,
    string Nombre,
    string EstadoConexion,
    string ModoOperacion,
    DateTimeOffset? ModoAtencionExpiraEn,
    bool LuzEncendida,
    int LuzNivel,
    decimal EscalaMasa,
    DateTimeOffset? UltimoHeartbeat,
    DateTimeOffset CreadoEn);

public sealed record BabyRecord(
    Guid Id,
    Guid FamilyId,
    Guid? CunaId,
    string Nombre,
    DateOnly FechaNacimiento,
    string? Notas,
    DateTimeOffset CreadoEn);

public sealed record CribSummaryResponse(
    CribRecord Cuna,
    IReadOnlyList<ClaimableDeviceRecord> Dispositivos,
    BabyRecord? Bebe);

public sealed record OpenPairingWindowResponse(
    DateTimeOffset ExpiraEn,
    int VentanaMinutos);

public sealed record ClaimDeviceRequest(
    string DeviceId,
    string CodigoReclamo,
    Guid? CunaId,
    string? NombreCuna);

public sealed record ClaimDeviceResponse(
    Guid DeviceRowId,
    string DeviceId,
    string Tipo,
    Guid CunaId,
    string NombreCuna,
    string DeviceTokenOnce);

public sealed record RenameCribRequest(string Nombre);

public sealed record UnlinkCribRequest(bool ConfirmacionExplicita, string TextoConfirmacion);

public sealed record UpsertBabyRequest(
    Guid CunaId,
    string Nombre,
    DateOnly FechaNacimiento,
    string? Notas);

public sealed record CribConfig(
    Guid CunaId,
    Guid FamilyId,
    decimal EscalaMasa,
    decimal TempMin,
    decimal TempMax,
    decimal HumMin,
    decimal HumMax,
    decimal MovimientoUmbral,
    bool MovimientoAlertaActiva,
    decimal RuidoMasaPiso);

public sealed record TelemetryRecord(
    Guid CunaId,
    Guid DispositivoId,
    DateTimeOffset Ts,
    decimal? MasaKg,
    decimal? IndiceMovimiento,
    decimal? TempC,
    decimal? Humedad,
    decimal? MasaBiberonG,
    decimal? TempBiberonC,
    int? BateriaPct,
    long Seq);

public sealed record EventAlertRecord(
    Guid Id,
    Guid CunaId,
    Guid DispositivoId,
    string Tipo,
    string Severidad,
    string? Mensaje,
    string? DatosJson,
    DateTimeOffset Ts);

public sealed record ToyUsageRecord(
    Guid Id,
    Guid CunaId,
    string Tipo,
    Guid? PerfilId,
    DateTimeOffset Inicio,
    int DuracionSegundos,
    int? Aciertos,
    string? Patron,
    string? Velocidad);

public sealed record CaregiverAuditLogEntry(
    Guid FamilyId,
    Guid? UsuarioId,
    Guid? PerfilId,
    string Accion,
    string? DetalleJson,
    DateTimeOffset Ts);

public sealed record SetCribModeRequest(
    string Modo,
    Guid? PerfilId = null);

public sealed record SetCribLightRequest(
    bool Encendida,
    int Nivel,
    Guid? PerfilId = null);

public sealed record UpdateCribThresholdsRequest(
    decimal TempMin,
    decimal TempMax,
    decimal HumMin,
    decimal HumMax,
    decimal MovimientoUmbral,
    bool MovimientoAlertaActiva,
    decimal RuidoMasaPiso,
    decimal EscalaMasa,
    Guid? PerfilId = null);

public sealed record LiveCribStatusResponse(
    CribRecord Cuna,
    CribConfig Umbrales,
    TelemetryRecord? UltimaTelemetria,
    IReadOnlyList<TelemetryRecord> TelemetriaReciente,
    IReadOnlyList<EventAlertRecord> AlertasRecientes,
    IReadOnlyList<ClaimableDeviceRecord> Dispositivos,
    bool EnVentanaGraciaRetiro);

public sealed record FeedingSessionRecord(
    Guid Id,
    Guid BebeId,
    Guid? PerfilId,
    decimal MasaInicialG,
    decimal? MasaFinalG,
    decimal? MlConsumidos,
    decimal? TempC,
    bool AlertaTemperatura,
    DateTimeOffset Inicio,
    DateTimeOffset? Fin);

public sealed record StartFeedingSessionRequest(
    Guid BebeId,
    decimal MasaInicialG,
    decimal? TempC,
    Guid? PerfilId = null);

public sealed record FinishFeedingSessionRequest(
    decimal MasaFinalG,
    Guid? PerfilId = null);

public sealed record SleepSessionRecord(
    Guid Id,
    Guid BebeId,
    Guid CunaId,
    string Tipo,
    DateTimeOffset Inicio,
    DateTimeOffset? Fin,
    decimal? MovimientoPromedio,
    int Interrupciones);

public sealed record CreateSleepSessionRequest(
    Guid CunaId,
    string Tipo,
    DateTimeOffset Inicio,
    DateTimeOffset? Fin,
    decimal? MovimientoPromedio,
    int Interrupciones = 0,
    Guid? PerfilId = null);

public sealed record SongCatalogItem(
    Guid Id,
    string Codigo,
    string Nombre,
    int DuracionSegundos,
    string NotasJson);

public sealed record PlaySongRequest(
    string Codigo,
    string Accion = "play",
    Guid? PerfilId = null);

public sealed record PlayMobileRequest(
    string Patron,
    string Velocidad,
    int DuracionSegundos,
    Guid? PerfilId = null);

public sealed record ToysAndPeripheralsStatusResponse(
    IReadOnlyList<ToyUsageRecord> HistorialUso,
    int? BateriaJuguetePct,
    bool AlertaBateriaBajaJuguete,
    int? BateriaBiberonPct,
    decimal? UltimaMasaBiberonG,
    decimal? UltimaTempBiberonC);

public sealed record ExportHistoryRequest(
    string Formato,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    Guid? PerfilId = null);

public sealed record ExportHistoryResponse(
    string FileName,
    string ContentType,
    string ContentBase64,
    int SleepSessionsCount,
    int FeedingSessionsCount,
    long ElapsedMs);

public sealed record ProtocolEnvelope(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("ts")] long? Ts,
    [property: JsonPropertyName("d")] JsonElement D);
