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

public sealed record ProtocolEnvelope(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("ts")] long? Ts,
    [property: JsonPropertyName("d")] JsonElement D);
