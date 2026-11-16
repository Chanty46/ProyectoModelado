using System.Buffers;
using System.Text;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Tcp;

/// <summary>
/// Lector TCP con buffers reutilizables (<see cref="ArrayPool{T}"/>) que evita syscalls byte a byte
/// e impone el framing delimitado por salto de línea (<c>\n</c>) con tope estricto de 4 KB por mensaje.
/// </summary>
/// <remarks>
/// Invariante de framing <c>\n</c> 4 KB: ninguna trama JSON de dispositivo puede exceder
/// <see cref="TcpProtocolParser.MaxLineBytes"/> (4096 bytes); si lo hace, el lector descarta bytes
/// hasta el siguiente <c>\n</c> para proteger la memoria del servidor y realinear el stream.
/// </remarks>
public sealed class TcpLineReader : IDisposable
{
    private readonly Stream _stream;
    private readonly byte[] _readBuffer;
    private readonly byte[] _lineBuffer;
    private int _readStart;
    private int _readEnd;
    private bool _disposed;

    public TcpLineReader(Stream stream, int bufferSize = 8192)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _readBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, TcpProtocolParser.MaxLineBytes));
        _lineBuffer = ArrayPool<byte>.Shared.Rent(TcpProtocolParser.MaxLineBytes);
    }

    private enum ChunkConsumeStatus
    {
        NeedMoreBytes,
        CompletedLine,
        ExceededWithNewlineConsumed,
        ExceededNeedDiscard
    }

    /// <summary>
    /// Lee la siguiente línea delimitada por <c>\n</c> respetando el límite máximo de 4 KB.
    /// </summary>
    public async Task<(string? Line, bool ExceededLimit, bool EndOfStream)> ReadLineAsync(CancellationToken ct)
    {
        var lineCount = 0;

        while (true)
        {
            if (_readStart >= _readEnd)
            {
                _readStart = 0;
                _readEnd = await _stream.ReadAsync(_readBuffer.AsMemory(0, _readBuffer.Length), ct);
                if (_readEnd == 0)
                {
                    return lineCount == 0
                        ? (null, false, true)
                        : (DecodeTrimmedLine(_lineBuffer, lineCount), false, false);
                }
            }

            switch (TryConsumeBufferedChunk(ref lineCount, out var completedLine))
            {
                case ChunkConsumeStatus.CompletedLine:
                    return (completedLine, false, false);
                case ChunkConsumeStatus.ExceededWithNewlineConsumed:
                    return (null, true, false);
                case ChunkConsumeStatus.ExceededNeedDiscard:
                    await DiscardUntilNewlineAsync(ct);
                    return (null, true, false);
            }
        }
    }

    private ChunkConsumeStatus TryConsumeBufferedChunk(ref int lineCount, out string? completedLine)
    {
        completedLine = null;

        var available = _readBuffer.AsSpan(_readStart, _readEnd - _readStart);
        var newlineIndex = available.IndexOf((byte)'\n');
        if (newlineIndex >= 0)
        {
            _readStart += newlineIndex + 1;
            if (lineCount + newlineIndex > TcpProtocolParser.MaxLineBytes)
            {
                return ChunkConsumeStatus.ExceededWithNewlineConsumed;
            }

            available[..newlineIndex].CopyTo(_lineBuffer.AsSpan(lineCount));
            lineCount += newlineIndex;
            completedLine = DecodeTrimmedLine(_lineBuffer, lineCount);
            return ChunkConsumeStatus.CompletedLine;
        }

        if (lineCount + available.Length > TcpProtocolParser.MaxLineBytes)
        {
            _readStart = _readEnd;
            return ChunkConsumeStatus.ExceededNeedDiscard;
        }

        available.CopyTo(_lineBuffer.AsSpan(lineCount));
        lineCount += available.Length;
        _readStart = _readEnd;
        return ChunkConsumeStatus.NeedMoreBytes;
    }

    private async Task DiscardUntilNewlineAsync(CancellationToken ct)
    {
        while (true)
        {
            _readStart = 0;
            _readEnd = await _stream.ReadAsync(_readBuffer.AsMemory(0, _readBuffer.Length), ct);
            if (_readEnd == 0)
            {
                return;
            }

            var discardNl = _readBuffer.AsSpan(0, _readEnd).IndexOf((byte)'\n');
            if (discardNl >= 0)
            {
                _readStart = discardNl + 1;
                return;
            }
        }
    }

    internal static string DecodeTrimmedLine(byte[] buffer, int length)
    {
        while (length > 0 && buffer[length - 1] == (byte)'\r')
        {
            length--;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_readBuffer);
        ArrayPool<byte>.Shared.Return(_lineBuffer);
    }
}

/// <summary>
/// Analizador y validador estricto del protocolo JSON sobre TCP (v=1) entre los nodos Pico W y el servidor local.
/// </summary>
public static class TcpProtocolParser
{
    /// <summary>
    /// Tamaño máximo permitido por trama JSON delimitada por <c>\n</c> (4 KB = 4096 bytes) para impedir
    /// agotamiento de memoria por mensajes sin terminador o payloads sobredimensionados.
    /// </summary>
    public const int MaxLineBytes = 4096;

    private static readonly IReadOnlyDictionary<string, (string Severity, string Message)> SimpleAlertDefinitions =
        new Dictionary<string, (string Severity, string Message)>(StringComparer.Ordinal)
        {
            ["removal"] = ("critica", "Retiro imprevisto del infante detectado en el colchón (RF04)."),
            ["env_out_of_range"] = ("advertencia", "Temperatura o humedad fuera del rango seguro durante >= 10 s (RF07)."),
            ["movement_high"] = ("advertencia", "Índice de movimiento anómalo sostenido >= 30 s (RF29)."),
            ["battery_low"] = ("advertencia", "Batería baja en periférico (< 10 %, RF36)."),
            ["button_attention"] = ("info", "Botón táctil de Modo Atención presionado en la unidad central (RF05)."),
            ["bottle_placed"] = ("info", "Biberón colocado sobre la base sensora (RF15).")
        };

    /// <summary>
    /// Lectura simple sin búfer adelantado pensada para clientes/pruebas que requieren leer una sola trama
    /// respetando el límite estricto de 4 KB por línea delimitada por <c>\n</c>.
    /// </summary>
    public static async Task<(string? Line, bool ExceededLimit, bool EndOfStream)> ReadBoundedLineAsync(
        Stream stream,
        CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxLineBytes);
        try
        {
            var count = 0;
            while (true)
            {
                if (count >= MaxLineBytes)
                {
                    // Consumir hasta el salto de línea o fin de stream para cerrar o rechazar limpiamente
                    while (await stream.ReadAsync(buffer.AsMemory(0, 1), ct) > 0)
                    {
                        if (buffer[0] == (byte)'\n')
                        {
                            break;
                        }
                    }

                    return (null, true, false);
                }

                var read = await stream.ReadAsync(buffer.AsMemory(count, 1), ct);
                if (read == 0)
                {
                    return count == 0
                        ? (null, false, true)
                        : (TcpLineReader.DecodeTrimmedLine(buffer, count), false, false);
                }

                if (buffer[count] == (byte)'\n')
                {
                    return (TcpLineReader.DecodeTrimmedLine(buffer, count), false, false);
                }

                count++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Valida el sobre base del protocolo (<c>v=1</c>, <c>t</c>, <c>seq</c>, <c>ts</c> y objeto <c>d</c>).
    /// </summary>
    public static bool TryParseEnvelope(string rawLine, out ProtocolEnvelope? envelope, out string? errorCode, out string? errorMsg)
    {
        envelope = null;
        errorCode = null;
        errorMsg = null;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            errorCode = "empty_line";
            errorMsg = "Línea vacía.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawLine);
            var root = doc.RootElement;

            if (!TryReadProtocolVersion(root, out var version, out errorCode, out errorMsg))
            {
                return false;
            }

            if (!TryReadRequiredString(root, "t", out var messageType))
            {
                errorCode = "invalid_envelope";
                errorMsg = "Falta tipo de mensaje 't'.";
                return false;
            }

            if (!TryReadOptionalSeq(root, out var seq, out errorCode, out errorMsg))
            {
                return false;
            }

            var ts = TryReadOptionalTimestamp(root);

            if (!root.TryGetProperty("d", out var dProp) || dProp.ValueKind != JsonValueKind.Object)
            {
                errorCode = "invalid_data";
                errorMsg = "El campo 'd' debe ser un objeto JSON.";
                return false;
            }

            envelope = new ProtocolEnvelope(version, messageType, seq, ts, dProp.Clone());
            return true;
        }
        catch (JsonException)
        {
            errorCode = "invalid_json";
            errorMsg = "Formato JSON inválido.";
            return false;
        }
    }

    /// <summary>
    /// Valida el payload <c>hello</c> verificando longitudes y que <c>device_type</c> sea un nodo conocido.
    /// </summary>
    public static bool TryValidateHello(
        JsonElement d,
        out string deviceId,
        out string deviceType,
        out string fwVersion,
        out string rawToken,
        out string? errorMsg)
    {
        deviceId = "";
        deviceType = "";
        fwVersion = "";
        rawToken = "";
        errorMsg = null;

        if (!TryReadRequiredString(d, "device_id", out var rawDeviceId))
        {
            errorMsg = "Falta 'device_id'.";
            return false;
        }

        if (!TryReadRequiredString(d, "device_type", out var rawDeviceType))
        {
            errorMsg = "Falta 'device_type'.";
            return false;
        }

        if (!TryReadRequiredString(d, "token", out var rawCandidateToken))
        {
            errorMsg = "Falta 'token'.";
            return false;
        }

        deviceId = rawDeviceId.Trim();
        deviceType = rawDeviceType.Trim();
        rawToken = rawCandidateToken.Trim();
        fwVersion = d.TryGetProperty("fw", out var fwProp) && fwProp.ValueKind == JsonValueKind.String
            ? (fwProp.GetString() ?? "1.0.0").Trim()
            : "1.0.0";

        if (deviceId.Length is < 3 or > 64)
        {
            errorMsg = "Longitud de 'device_id' fuera de rango.";
            return false;
        }

        if (!DeviceTypes.All.Contains(deviceType))
        {
            errorMsg = "Tipo de dispositivo no reconocido.";
            return false;
        }

        if (rawToken.Length is < 8 or > 256)
        {
            errorMsg = "Longitud de 'token' inválida.";
            return false;
        }

        if (fwVersion.Length is < 1 or > 32)
        {
            errorMsg = "Longitud de 'fw' inválida.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Valida rangos físicos de telemetría por tipo de nodo usando <c>cunaId</c> y <c>dispositivoRowId</c>
    /// deducidos de la base de datos (nunca del cuerpo JSON del mensaje).
    /// </summary>
    public static bool TryValidateTelemetry(
        string deviceType,
        Guid cunaId,
        Guid dispositivoRowId,
        long seq,
        DateTimeOffset ts,
        JsonElement d,
        out TelemetryRecord? record,
        out string? errorMsg)
    {
        record = null;
        errorMsg = null;

        if (!TryReadOptionalBatteryPct(d, out var batteryPct, out errorMsg))
        {
            return false;
        }

        decimal? masaKg = null;
        decimal? movementIdx = null;
        decimal? tempC = null;
        decimal? humPct = null;
        decimal? masaBiberonG = null;
        decimal? tempBiberonC = null;

        switch (deviceType)
        {
            case DeviceTypes.Central:
                if (!TryReadDecimal(d, "temp_c", -10.0m, 60.0m, out var tCentral) ||
                    !TryReadDecimal(d, "hum_pct", 0.0m, 100.0m, out var hCentral))
                {
                    errorMsg = "Telemetría de central requiere 'temp_c' (-10..60) y 'hum_pct' (0..100).";
                    return false;
                }
                tempC = tCentral;
                humPct = hCentral;
                break;

            case DeviceTypes.Colchon:
                if (!TryReadDecimal(d, "mass_kg", 0.0m, 30.0m, out var mColchon) ||
                    !TryReadDecimal(d, "movement_idx", 0.0m, 50.0m, out var movColchon))
                {
                    errorMsg = "Telemetría de colchón requiere 'mass_kg' (0..30) y 'movement_idx' (0..50).";
                    return false;
                }
                masaKg = mColchon;
                movementIdx = movColchon;
                break;

            case DeviceTypes.Biberon:
                if (!TryReadDecimal(d, "mass_g", 0.0m, 2000.0m, out var mBib) ||
                    !TryReadDecimal(d, "temp_c", 0.0m, 100.0m, out var tBib))
                {
                    errorMsg = "Telemetría de biberón requiere 'mass_g' (0..2000) y 'temp_c' (0..100).";
                    return false;
                }
                masaBiberonG = mBib;
                tempBiberonC = tBib;
                break;

            case DeviceTypes.Juguete:
                if (batteryPct is null)
                {
                    errorMsg = "Telemetría de juguete requiere 'battery_pct' (0..100).";
                    return false;
                }
                break;

            default:
                errorMsg = "Este tipo de dispositivo no emite telemetría periódica.";
                return false;
        }

        record = new TelemetryRecord(
            cunaId,
            dispositivoRowId,
            ts,
            masaKg,
            movementIdx,
            tempC,
            humPct,
            masaBiberonG,
            tempBiberonC,
            batteryPct,
            seq);
        return true;
    }

    /// <summary>
    /// Valida eventos asíncronos del dispositivo y construye la alerta o el registro de uso de juguete correspondiente.
    /// </summary>
    public static bool TryValidateEvent(
        Guid cunaId,
        Guid dispositivoRowId,
        DateTimeOffset ts,
        JsonElement d,
        out EventAlertRecord? alertRecord,
        out ToyUsageRecord? toyUsageRecord,
        out string? errorMsg)
    {
        alertRecord = null;
        toyUsageRecord = null;
        errorMsg = null;

        if (!TryReadRequiredString(d, "kind", out var rawKind))
        {
            errorMsg = "Evento requiere campo 'kind'.";
            return false;
        }

        var kind = rawKind.Trim();
        if (SimpleAlertDefinitions.TryGetValue(kind, out var alertDef))
        {
            alertRecord = new EventAlertRecord(
                Guid.NewGuid(),
                cunaId,
                dispositivoRowId,
                kind,
                alertDef.Severity,
                alertDef.Message,
                d.GetRawText(),
                ts);
            return true;
        }

        return kind switch
        {
            "game_end" => TryBuildGameEndEvent(cunaId, ts, d, out toyUsageRecord, out errorMsg),
            "mobile_session_end" => TryBuildMobileSessionEndEvent(cunaId, ts, d, out toyUsageRecord, out errorMsg),
            _ => FailUnknownEvent(kind, out errorMsg)
        };
    }

    private static bool TryReadProtocolVersion(
        JsonElement root,
        out int version,
        out string? errorCode,
        out string? errorMsg)
    {
        version = 0;
        errorCode = null;
        errorMsg = null;

        if (!root.TryGetProperty("v", out var vProp) || vProp.ValueKind != JsonValueKind.Number || !vProp.TryGetInt32(out version))
        {
            errorCode = "invalid_envelope";
            errorMsg = "Falta versión 'v' numérica.";
            return false;
        }

        if (version != 1)
        {
            errorCode = "unsupported_version";
            errorMsg = $"Versión de protocolo v={version} no soportada.";
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalSeq(
        JsonElement root,
        out long seq,
        out string? errorCode,
        out string? errorMsg)
    {
        seq = 0;
        errorCode = null;
        errorMsg = null;

        if (!root.TryGetProperty("seq", out var seqProp) || seqProp.ValueKind != JsonValueKind.Number)
        {
            return true;
        }

        if (!seqProp.TryGetInt64(out seq) || seq < 0)
        {
            errorCode = "invalid_seq";
            errorMsg = "El campo 'seq' debe ser un entero no negativo.";
            return false;
        }

        return true;
    }

    private static long? TryReadOptionalTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("ts", out var tsProp) &&
            tsProp.ValueKind == JsonValueKind.Number &&
            tsProp.TryGetInt64(out var parsedTs))
        {
            return parsedTs;
        }

        return null;
    }

    private static bool TryReadRequiredString(JsonElement obj, string propName, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString() ?? "";
        return true;
    }

    private static bool TryReadOptionalBatteryPct(JsonElement d, out int? batteryPct, out string? errorMsg)
    {
        batteryPct = null;
        errorMsg = null;

        if (!d.TryGetProperty("battery_pct", out var batProp) || batProp.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (batProp.ValueKind != JsonValueKind.Number || !batProp.TryGetInt32(out var b) || b is < 0 or > 100)
        {
            errorMsg = "Campo 'battery_pct' fuera de rango (0..100).";
            return false;
        }

        batteryPct = b;
        return true;
    }

    private static bool TryBuildGameEndEvent(
        Guid cunaId,
        DateTimeOffset ts,
        JsonElement d,
        out ToyUsageRecord? toyUsageRecord,
        out string? errorMsg)
    {
        toyUsageRecord = null;
        errorMsg = null;

        if (!d.TryGetProperty("hits", out var hitsProp) || !hitsProp.TryGetInt32(out var hits) || hits < 0 ||
            !d.TryGetProperty("secs", out var secsProp) || !secsProp.TryGetInt32(out var secs) || secs is < 0 or > 86400)
        {
            errorMsg = "Evento 'game_end' requiere 'hits' >= 0 y 'secs' (0..86400).";
            return false;
        }

        toyUsageRecord = new ToyUsageRecord(
            Guid.NewGuid(), cunaId, "botones", null, ts.AddSeconds(-secs), secs, hits, null, null);
        return true;
    }

    private static bool TryBuildMobileSessionEndEvent(
        Guid cunaId,
        DateTimeOffset ts,
        JsonElement d,
        out ToyUsageRecord? toyUsageRecord,
        out string? errorMsg)
    {
        toyUsageRecord = null;
        errorMsg = null;

        if (!d.TryGetProperty("secs", out var mSecsProp) || !mSecsProp.TryGetInt32(out var mSecs) || mSecs is < 0 or > 86400)
        {
            errorMsg = "Evento 'mobile_session_end' requiere 'secs' (0..86400).";
            return false;
        }

        var pattern = d.TryGetProperty("pattern", out var patProp) && patProp.ValueKind == JsonValueKind.String
            ? patProp.GetString()
            : "onda";
        var speed = d.TryGetProperty("speed", out var spdProp) && spdProp.ValueKind == JsonValueKind.String
            ? spdProp.GetString()
            : "media";
        if (speed is not ("lenta" or "media" or "rapida"))
        {
            errorMsg = "Velocidad de móvil inválida (debe ser lenta, media o rapida).";
            return false;
        }

        toyUsageRecord = new ToyUsageRecord(
            Guid.NewGuid(), cunaId, "movil", null, ts.AddSeconds(-mSecs), mSecs, null, pattern, speed);
        return true;
    }

    private static bool FailUnknownEvent(string kind, out string? errorMsg)
    {
        errorMsg = $"Tipo de evento '{kind}' no reconocido.";
        return false;
    }

    private static bool TryReadDecimal(JsonElement obj, string propName, decimal min, decimal max, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return prop.TryGetDecimal(out value) && value >= min && value <= max;
    }
}
