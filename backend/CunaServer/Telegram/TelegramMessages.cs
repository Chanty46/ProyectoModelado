using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CunaServer.Data;

namespace CunaServer.Telegram;

/// <summary>
/// Textos que envía el bot de Telegram. Se envían como texto plano (sin parse_mode),
/// así los nombres que escribe el usuario no pueden romper el formato.
/// </summary>
public static class TelegramMessages
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CR");
    private static readonly TimeZoneInfo LocalZone = ResolveLocalZone();

    public const string Welcome =
        "👶 ¡Hola! Soy el bot de Cuna Inteligente.\n" +
        "\n" +
        "Te aviso al instante si pasa algo en la cuna y te digo cómo está cuando me lo pidas.\n" +
        "\n" +
        "Para empezar, vincula tu cuenta:\n" +
        "1. Entra a la página web.\n" +
        "2. Ve a «Telegram y Recordatorios» y pulsa «Generar código».\n" +
        "3. Envíame aquí el comando que te aparece, por ejemplo:\n" +
        "     /vincular TG-ABC123\n" +
        "\n" +
        "Escribe /ayuda para ver todo lo que puedo hacer.";

    public const string Help =
        "📖 Comandos disponibles\n" +
        "\n" +
        "/estado\n" +
        "     Cómo está la cuna ahora: bebé, temperatura, humedad y última toma.\n" +
        "\n" +
        "/vincular CÓDIGO\n" +
        "     Conecta este chat con tu cuenta. El código se genera en la web y dura 60 minutos.\n" +
        "\n" +
        "/ayuda\n" +
        "     Muestra este mensaje.\n" +
        "\n" +
        "🔔 Las alertas (bebé fuera de la cuna, temperatura, conexión perdida…) te llegan solas, no tienes que pedirlas.";

    public const string LinkMissingCode =
        "⚠️ Falta el código.\n" +
        "\n" +
        "Escríbelo así:\n" +
        "     /vincular TG-ABC123\n" +
        "\n" +
        "Lo generas en la web, en «Telegram y Recordatorios».";

    public const string LinkFailed =
        "❌ No pude vincular tu cuenta.\n" +
        "\n" +
        "El código es inválido, ya se usó o caducó (dura 60 minutos).\n" +
        "Genera uno nuevo en la web y vuelve a enviarlo.";

    public const string LinkOk =
        "✅ ¡Listo! Este chat quedó vinculado correctamente a tu familia.\n" +
        "\n" +
        "Desde ahora te llegarán aquí las alertas de la cuna.\n" +
        "Escribe /estado para ver cómo está ahora mismo.";

    public const string AccessDenied =
        "🔒 Acceso denegado.\n" +
        "\n" +
        "Este chat no está vinculado a ninguna familia, o tu acceso fue retirado.\n" +
        "Si eres parte de la familia, genera un código en la web y usa /vincular.";

    public const string NoCribs =
        "ℹ️ Tu familia todavía no tiene cunas registradas.\n" +
        "\n" +
        "Agrega la cuna desde la web, en «Gestión de Cunas».";

    public const string UnknownCommand =
        "🤔 No entendí ese mensaje.\n" +
        "\n" +
        "Prueba con:\n" +
        "/estado — cómo está la cuna\n" +
        "/ayuda — todos los comandos";

    public sealed record CribStatus(
        CribRecord Crib,
        BabyRecord? Baby,
        decimal? TempC,
        decimal? Humidity,
        decimal? MassKg,
        DateTimeOffset? LastReadingAt,
        FeedingSessionRecord? LastFeeding,
        decimal? BottleTempC = null,
        decimal? BottleMassG = null);

    public static string Status(IReadOnlyList<CribStatus> cribs, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.Append("📋 Estado de la cuna · ").AppendLine(FormatTime(now));

        foreach (var s in cribs)
        {
            sb.AppendLine();
            sb.Append("🛏️ ").AppendLine(s.Crib.Nombre);
            sb.AppendLine(ConnectionAndMode(s.Crib));
            sb.AppendLine(BabyLine(s));

            sb.Append("🌡️ Temperatura: ")
              .AppendLine(s.TempC is { } t ? $"{t.ToString("0.0", Culture)} °C" : "sin datos");
            sb.Append("💧 Humedad: ")
              .AppendLine(s.Humidity is { } h ? $"{h.ToString("0", Culture)} %" : "sin datos");
            sb.Append("💡 Luz nocturna: ")
              .AppendLine(s.Crib.LuzEncendida ? $"encendida al {s.Crib.LuzNivel} %" : "apagada");
            if (s.BottleTempC is not null || s.BottleMassG is not null)
            {
                var bottleTemp = s.BottleTempC is { } bt ? $"{bt.ToString("0.0", Culture)} °C" : "sin datos";
                var bottleMass = s.BottleMassG is { } bm ? $"{bm.ToString("0", Culture)} g" : "sin datos";
                sb.AppendLine($"🍼 Biberón: {bottleTemp} · {bottleMass}");
            }
            sb.AppendLine(FeedingLine(s.LastFeeding, now));

            if (s.LastReadingAt is { } at)
            {
                sb.Append("🕒 Última lectura: ").AppendLine(Ago(at, now));
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string Alert(CribRecord crib, EventAlertRecord alert)
    {
        var (title, body) = DescribeAlert(alert);
        var icon = alert.Severidad switch
        {
            "critica" => "🚨",
            "advertencia" => "⚠️",
            _ => "ℹ️"
        };

        var sb = new StringBuilder();
        sb.Append(icon).Append(' ').AppendLine(title);
        sb.AppendLine();
        sb.Append("🛏️ ").AppendLine(crib.Nombre);
        sb.AppendLine(body);
        sb.AppendLine();
        sb.Append("🕒 ").Append(FormatTime(alert.Ts));
        return sb.ToString();
    }

    public static string FeedingReminder(int intervalHours)
    {
        var hours = intervalHours == 1 ? "1 hora" : $"{intervalHours} horas";
        return
            "🍼 Recordatorio de alimentación\n" +
            "\n" +
            $"Ya pasaron {hours} desde la última toma registrada.\n" +
            "Cuando alimentes al bebé, regístralo en la web y el contador vuelve a empezar.";
    }

    // ---------------------------------------------------------------------

    private static (string Title, string Body) DescribeAlert(EventAlertRecord alert)
    {
        var data = ParseData(alert.DatosJson);
        return alert.Tipo switch
        {
            "removal_alarm" or "removal" => (
                "ALERTA: el bebé salió de la cuna",
                "Se detectó que el bebé fue retirado del colchón sin activar el Modo Atención.\n" +
                "👉 Revisa la cuna de inmediato. Si fuiste tú, activa el Modo Atención antes de sacarlo."),

            "watchdog_offline" => (
                "ALERTA: la cuna perdió la conexión",
                "La unidad central dejó de comunicarse hace más de 1 minuto, así que no se está vigilando la cuna.\n" +
                "👉 Revisa que la cuna tenga corriente y Wi-Fi."),

            "env_out_of_range" => (
                "Temperatura o humedad fuera de rango",
                EnvironmentDetail(data) +
                "👉 Revisa la ventilación o el clima de la habitación."),

            "movement_high" => (
                "Mucho movimiento en la cuna",
                "El bebé lleva más de 30 segundos moviéndose más de lo normal.\n" +
                "👉 Puede estar inquieto o despierto. Échale un vistazo."),

            "food_temp_out_of_range" => (
                "Temperatura del biberón no recomendada",
                FoodTempDetail(data) +
                "La temperatura segura está entre 35.0 y 38.5 °C.\n" +
                "👉 Espera a que se enfríe o caliéntalo un poco antes de darlo."),

            "battery_low" => (
                "Batería baja",
                (TryGetDecimal(data, "battery_pct") is { } pct
                    ? $"A un dispositivo le queda {pct.ToString("0", Culture)} % de batería.\n"
                    : "A un dispositivo le queda menos del 10 % de batería.\n") +
                "👉 Cambia las pilas pronto para que siga funcionando."),

            _ => (
                alert.Severidad == "critica" ? "ALERTA" : "Aviso de la cuna",
                CleanTechnicalText(alert.Mensaje ?? alert.Tipo))
        };
    }

    private static string EnvironmentDetail(JsonElement? data)
    {
        var temp = TryGetDecimal(data, "temp_c");
        var hum = TryGetDecimal(data, "hum_pct");
        var sb = new StringBuilder("La habitación lleva más de 10 segundos fuera de los límites configurados.\n");
        if (temp is not null || hum is not null)
        {
            sb.Append("Ahora mismo: ");
            if (temp is { } t) sb.Append($"{t.ToString("0.0", Culture)} °C");
            if (temp is not null && hum is not null) sb.Append(" · ");
            if (hum is { } h) sb.Append($"{h.ToString("0", Culture)} % de humedad");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FoodTempDetail(JsonElement? data)
        => TryGetDecimal(data, "temp_c") is { } t
            ? $"El biberón está a {t.ToString("0.0", Culture)} °C.\n"
            : "";

    private static string ConnectionAndMode(CribRecord crib)
    {
        var online = crib.EstadoConexion == "en_linea";

        // La alarma va siempre primero: no se puede esconder detrás de "desconectada".
        if (crib.ModoOperacion == "alarma")
        {
            return online
                ? "🚨 ¡ALARMA ACTIVA! Revisa la cuna"
                : "🚨 ¡ALARMA ACTIVA! Revisa la cuna\n🔴 Además la cuna está desconectada";
        }

        if (!online)
        {
            return "🔴 Desconectada: ahora mismo no se está vigilando";
        }

        return crib.ModoOperacion == "atencion"
            ? crib.ModoAtencionExpiraEn is { } until
                ? $"🟡 Modo Atención: alarmas en pausa hasta las {FormatTime(until)}"
                : "🟡 Modo Atención: alarmas en pausa"
            : "🟢 Conectada y vigilando";
    }

    private static string BabyLine(CribStatus s)
    {
        var name = s.Baby?.Nombre ?? "Bebé";
        if (s.MassKg is not { } mass)
        {
            return $"👶 {name}: sin datos del colchón";
        }

        // D14: la presencia se evalúa con el umbral escalado (masa > escala_masa kg; 0.20 kg con la escala por defecto).
        var present = mass > s.Crib.EscalaMasa;
        return present ? $"👶 {name}: en la cuna" : $"👶 {name}: fuera de la cuna";
    }

    private static string FeedingLine(FeedingSessionRecord? last, DateTimeOffset now)
    {
        if (last is null)
        {
            return "🍼 Última toma: sin registros";
        }

        var when = Ago(last.Fin ?? last.Inicio, now);
        if (last.Fin is null)
        {
            return $"🍼 Toma en curso (empezó {when})";
        }

        return last.MlConsumidos is { } ml
            ? $"🍼 Última toma: {when} ({ml.ToString("0", Culture)} ml)"
            : $"🍼 Última toma: {when}";
    }

    private static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var diff = now - at;
        if (diff < TimeSpan.FromMinutes(1)) return "hace menos de 1 min";
        if (diff < TimeSpan.FromHours(1)) return $"hace {(int)diff.TotalMinutes} min";
        if (diff < TimeSpan.FromDays(1))
        {
            var h = (int)diff.TotalHours;
            var m = diff.Minutes;
            return m == 0 ? $"hace {h} h" : $"hace {h} h {m} min";
        }
        return $"el {TimeZoneInfo.ConvertTime(at, LocalZone).ToString("d 'de' MMMM", Culture)}";
    }

    private static string FormatTime(DateTimeOffset at)
        => TimeZoneInfo.ConvertTime(at, LocalZone).ToString("h:mm tt", Culture);

    /// <summary>Quita referencias internas como "(RF04)" de textos pensados para el equipo.</summary>
    private static string CleanTechnicalText(string text)
        => Regex.Replace(text, @"\s*\((?:RF|RNF|D)\d+[^)]*\)", "").Trim();

    private static JsonElement? ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryGetDecimal(JsonElement? data, string name)
        => data is { } d && d.TryGetProperty(name, out var p) && p.TryGetDecimal(out var v) ? v : null;

    private static TimeZoneInfo ResolveLocalZone()
    {
        foreach (var id in new[] { "America/Costa_Rica", "Central America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("CR", TimeSpan.FromHours(-6), "Costa Rica", "Costa Rica");
    }
}
