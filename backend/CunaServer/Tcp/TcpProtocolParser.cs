using System.Text;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Tcp;

public static class TcpProtocolParser
{
    public const int MaxLineBytes = 4096;

    public static async Task<(string? Line, bool ExceededLimit, bool EndOfStream)> ReadBoundedLineAsync(
        Stream stream,
        CancellationToken ct)
    {
        var buffer = new byte[MaxLineBytes];
        var count = 0;
        var singleByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(singleByte.AsMemory(0, 1), ct);
            if (read == 0)
            {
                if (count == 0)
                {
                    return (null, false, true);
                }

                return (Encoding.UTF8.GetString(buffer, 0, count).TrimEnd('\r'), false, false);
            }

            var b = singleByte[0];
            if (b == (byte)'\n')
            {
                return (Encoding.UTF8.GetString(buffer, 0, count).TrimEnd('\r'), false, false);
            }

            if (count >= MaxLineBytes)
            {
                // Consumir hasta el salto de línea o fin de stream para cerrar o rechazar limpiamente
                while (await stream.ReadAsync(singleByte.AsMemory(0, 1), ct) > 0)
                {
                    if (singleByte[0] == (byte)'\n')
                    {
                        break;
                    }
                }

                return (null, true, false);
            }

            buffer[count++] = b;
        }
    }

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

            if (!root.TryGetProperty("v", out var vProp) || vProp.ValueKind != JsonValueKind.Number || !vProp.TryGetInt32(out var v))
            {
                errorCode = "invalid_envelope";
                errorMsg = "Falta versión 'v' numérica.";
                return false;
            }

            if (v != 1)
            {
                errorCode = "unsupported_version";
                errorMsg = $"Versión de protocolo v={v} no soportada.";
                return false;
            }

            if (!root.TryGetProperty("t", out var tProp) || tProp.ValueKind != JsonValueKind.String)
            {
                errorCode = "invalid_envelope";
                errorMsg = "Falta tipo de mensaje 't'.";
                return false;
            }

            var t = tProp.GetString() ?? "";
            long seq = 0;
            if (root.TryGetProperty("seq", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number)
            {
                if (!seqProp.TryGetInt64(out seq) || seq < 0)
                {
                    errorCode = "invalid_seq";
                    errorMsg = "El campo 'seq' debe ser un entero no negativo.";
                    return false;
                }
            }

            long? ts = null;
            if (root.TryGetProperty("ts", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number)
            {
                if (tsProp.TryGetInt64(out var parsedTs))
                {
                    ts = parsedTs;
                }
            }

            if (!root.TryGetProperty("d", out var dProp) || dProp.ValueKind != JsonValueKind.Object)
            {
                errorCode = "invalid_data";
                errorMsg = "El campo 'd' debe ser un objeto JSON.";
                return false;
            }

            envelope = new ProtocolEnvelope(v, t, seq, ts, dProp.Clone());
            return true;
        }
        catch (JsonException)
        {
            errorCode = "invalid_json";
            errorMsg = "Formato JSON inválido.";
            return false;
        }
    }

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

        if (!d.TryGetProperty("device_id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
        {
            errorMsg = "Falta 'device_id'.";
            return false;
        }

        if (!d.TryGetProperty("device_type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            errorMsg = "Falta 'device_type'.";
            return false;
        }

        if (!d.TryGetProperty("token", out var tokenProp) || tokenProp.ValueKind != JsonValueKind.String)
        {
            errorMsg = "Falta 'token'.";
            return false;
        }

        deviceId = (idProp.GetString() ?? "").Trim();
        deviceType = (typeProp.GetString() ?? "").Trim();
        rawToken = (tokenProp.GetString() ?? "").Trim();
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

        decimal? masaKg = null;
        decimal? movementIdx = null;
        decimal? tempC = null;
        decimal? humPct = null;
        decimal? masaBiberonG = null;
        decimal? tempBiberonC = null;
        int? batteryPct = null;

        if (d.TryGetProperty("battery_pct", out var batProp) && batProp.ValueKind != JsonValueKind.Null)
        {
            if (batProp.ValueKind != JsonValueKind.Number || !batProp.TryGetInt32(out var b) || b is < 0 or > 100)
            {
                errorMsg = "Campo 'battery_pct' fuera de rango (0..100).";
                return false;
            }
            batteryPct = b;
        }

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

        if (!d.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
        {
            errorMsg = "Evento requiere campo 'kind'.";
            return false;
        }

        var kind = (kindProp.GetString() ?? "").Trim();
        switch (kind)
        {
            case "removal":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "critica",
                    "Retiro imprevisto del infante detectado en el colchón (RF04).",
                    d.GetRawText(), ts);
                return true;

            case "env_out_of_range":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "advertencia",
                    "Temperatura o humedad fuera del rango seguro durante >= 10 s (RF07).",
                    d.GetRawText(), ts);
                return true;

            case "movement_high":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "advertencia",
                    "Índice de movimiento anómalo sostenido >= 30 s (RF29).",
                    d.GetRawText(), ts);
                return true;

            case "battery_low":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "advertencia",
                    "Batería baja en periférico (< 10 %, RF36).",
                    d.GetRawText(), ts);
                return true;

            case "button_attention":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "info",
                    "Botón táctil de Modo Atención presionado en la unidad central (RF05).",
                    d.GetRawText(), ts);
                return true;

            case "bottle_placed":
                alertRecord = new EventAlertRecord(
                    Guid.NewGuid(), cunaId, dispositivoRowId, kind, "info",
                    "Biberón colocado sobre la base sensora (RF15).",
                    d.GetRawText(), ts);
                return true;

            case "game_end":
                if (!d.TryGetProperty("hits", out var hitsProp) || !hitsProp.TryGetInt32(out var hits) || hits < 0 ||
                    !d.TryGetProperty("secs", out var secsProp) || !secsProp.TryGetInt32(out var secs) || secs is < 0 or > 86400)
                {
                    errorMsg = "Evento 'game_end' requiere 'hits' >= 0 y 'secs' (0..86400).";
                    return false;
                }
                toyUsageRecord = new ToyUsageRecord(
                    Guid.NewGuid(), cunaId, "botones", null, ts.AddSeconds(-secs), secs, hits, null, null);
                return true;

            case "mobile_session_end":
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

            default:
                errorMsg = $"Tipo de evento '{kind}' no reconocido.";
                return false;
        }
    }

    private static bool TryReadDecimal(JsonElement obj, string propName, decimal min, decimal max, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!prop.TryGetDecimal(out value))
        {
            return false;
        }

        return value >= min && value <= max;
    }
}
