using System.Text;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;
using Xunit;

namespace CunaServer.Tests;

public class TelegramMessagesAndExporterTests
{
    [Fact]
    public void TelegramMessages_Status_PrioritizesAlarmWhenOffline_AndRespectsEscalaMasa()
    {
        var now = new DateTimeOffset(2026, 11, 17, 18, 30, 0, TimeSpan.Zero);
        var cribAlarmOffline = new CribRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cuna Principal",
            "desconectada",
            "alarma",
            null,
            LuzEncendida: true,
            LuzNivel: 45,
            EscalaMasa: 0.20m,
            UltimoHeartbeat: now.AddMinutes(-3),
            CreadoEn: now.AddDays(-2));

        var baby = new BabyRecord(
            Guid.NewGuid(),
            cribAlarmOffline.FamilyId,
            cribAlarmOffline.Id,
            "Sofía",
            new DateOnly(2026, 6, 1),
            null,
            now.AddDays(-2));

        var activeFeeding = new FeedingSessionRecord(
            Guid.NewGuid(),
            baby.Id,
            null,
            180m,
            null,
            null,
            37.0m,
            false,
            now.AddMinutes(-12),
            null);

        var statusOutside = new TelegramMessages.CribStatus(
            cribAlarmOffline,
            baby,
            TempC: 23.2m,
            Humidity: 52m,
            MassKg: 0.15m,
            LastReadingAt: now.AddMinutes(-3),
            LastFeeding: activeFeeding,
            BottleTempC: 37.0m,
            BottleMassG: 180m);

        var text = TelegramMessages.Status([statusOutside], now);

        Assert.Contains("¡ALARMA ACTIVA!", text);
        Assert.Contains("Además la cuna está desconectada", text);
        Assert.Contains("Sofía: fuera de la cuna", text);
        Assert.Contains("encendida al 45 %", text);
        Assert.Contains("Toma en curso", text);
    }

    [Fact]
    public void TelegramMessages_Alert_FormatsEveryVariantAndStripsRfCodes()
    {
        var now = DateTimeOffset.UtcNow;
        var crib = new CribRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cuna Sala",
            "en_linea",
            "monitoreo",
            null,
            false,
            0,
            0.20m,
            now,
            now.AddDays(-1));

        var envAlert = new EventAlertRecord(
            Guid.NewGuid(), crib.Id, Guid.Empty, "env_out_of_range", "advertencia",
            "Desvío térmico (RF07)", """{"temp_c":29.4,"hum_pct":74}""", now);
        var envMsg = TelegramMessages.Alert(crib, envAlert);
        Assert.Contains("Temperatura o humedad fuera de rango", envMsg);
        Assert.Contains("74 % de humedad", envMsg);

        var fallbackAlert = new EventAlertRecord(
            Guid.NewGuid(), crib.Id, Guid.Empty, "custom_notice", "info",
            "Evento de prueba (RF04) con detalle (D14)", null, now);
        var fallbackMsg = TelegramMessages.Alert(crib, fallbackAlert);
        Assert.Contains("Evento de prueba con detalle", fallbackMsg);
        Assert.DoesNotContain("RF04", fallbackMsg);
        Assert.DoesNotContain("D14", fallbackMsg);
    }

    [Fact]
    public void HistoryReportExporter_NeutralizesCsvFormulas_AndBuildsMultiPageWinAnsiPdf()
    {
        var now = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
        var baby = new BabyRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "=CMD|'/C calc'!A0 José",
            new DateOnly(2026, 5, 10),
            "+2+2 notas clínicas @admin",
            now.AddDays(-10));

        var sleeps = Enumerable.Range(0, 30)
            .Select(i => new SleepSessionRecord(
                Guid.NewGuid(),
                baby.Id,
                baby.CunaId!.Value,
                i % 2 == 0 ? "siesta" : "noche",
                now.AddHours(-(i + 2)),
                now.AddHours(-(i + 1)),
                0.04m,
                i % 3))
            .ToList();

        var feedings = Enumerable.Range(0, 25)
            .Select(i => new FeedingSessionRecord(
                Guid.NewGuid(),
                baby.Id,
                null,
                160m,
                95m,
                63.1m,
                36.8m,
                i == 0,
                now.AddHours(-(i + 3)),
                now.AddHours(-(i + 2))))
            .ToList();

        var (csvBytes, csvType, csvName) = HistoryReportExporter.BuildReport("csv", baby, sleeps, feedings, now);
        var csv = Encoding.UTF8.GetString(csvBytes);

        Assert.Equal("text/csv; charset=utf-8", csvType);
        Assert.EndsWith(".csv", csvName);
        Assert.Contains("'=CMD", csv);

        var (pdfBytes, pdfType, pdfName) = HistoryReportExporter.BuildReport("pdf", baby, sleeps, feedings, now);
        var pdfAscii = Encoding.ASCII.GetString(pdfBytes);

        Assert.Equal("application/pdf", pdfType);
        Assert.EndsWith(".pdf", pdfName);
        Assert.StartsWith("%PDF-1.4", pdfAscii);
        Assert.Contains("%%EOF", pdfAscii);
        // 55 registros superan 1 página de 38 líneas, verificando paginación múltiple
        Assert.Contains("/Count 2", pdfAscii);
    }
}
