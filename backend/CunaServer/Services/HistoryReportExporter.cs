using System.Globalization;
using System.Text;
using CunaServer.Data;

namespace CunaServer.Services;

/// <summary>
/// Generador dedicado de reportes históricos (RF32) en formato CSV y PDF 1.4 puro.
/// Encapsula la sanitización contra inyección de fórmulas CSV y la paginación/cálculo
/// de anchos métricos Helvetica/WinAnsiEncoding sin dependencias externas.
/// </summary>
public static class HistoryReportExporter
{
    private const int MaxPdfCharsPerLine = 92;
    private const float MaxPdfWidthPt = 500f;
    private const int MaxBabyHeaderLines = 8;
    private const int PageTopY = 760;
    private const int LineHeightPt = 16;
    private const int HeaderBodyGapPt = 8;
    private const int BottomMarginPt = 50;
    private const int MaxLinesPerPage = 38;
    private const float DefaultFontSizePt = 10f;

    /// <summary>
    /// Construye el archivo binario del reporte en el formato solicitado ('csv' o 'pdf')
    /// junto con su tipo MIME y nombre de descarga sanitizado.
    /// </summary>
    public static (byte[] FileBytes, string ContentType, string FileName) BuildReport(
        string format,
        BabyRecord baby,
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions,
        DateTimeOffset generatedAtUtc)
    {
        var safeBabySlug = SanitizeFileName(baby.Nombre);
        if (format == "csv")
        {
            return (
                BuildCsvBytes(baby, sleepSessions, feedingSessions),
                "text/csv; charset=utf-8",
                $"historial_{safeBabySlug}_{generatedAtUtc:yyyyMMdd}.csv");
        }

        return (
            BuildPdfBytes(baby, sleepSessions, feedingSessions, generatedAtUtc),
            "application/pdf",
            $"historial_{safeBabySlug}_{generatedAtUtc:yyyyMMdd}.pdf");
    }

    /// <summary>
    /// Genera el reporte CSV en UTF-8 con escape y neutralización de fórmulas en campos de texto.
    /// </summary>
    public static byte[] BuildCsvBytes(
        BabyRecord baby,
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Categoria,FechaInicio,FechaFin,Detalle1,Detalle2,Detalle3,Alerta");

        foreach (var s in sleepSessions)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Sueno ({s.Tipo}),{s.Inicio:O},{s.Fin?.ToString("O") ?? ""},mov_prom={s.MovimientoPromedio ?? 0:F3},interrupciones={s.Interrupciones},bebe={EscapeCsv(baby.Nombre)},NO"));
        }

        foreach (var f in feedingSessions)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Alimentacion,{f.Inicio:O},{f.Fin?.ToString("O") ?? ""},m_inicial_g={f.MasaInicialG:F1},m_final_g={f.MasaFinalG ?? 0:F1},ml={f.MlConsumidos ?? 0:F1},{(f.AlertaTemperatura ? "SI_TEMP" : "NO")}"));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Genera un documento PDF 1.4 válido usando fuente estándar Type1 Helvetica en WinAnsiEncoding
    /// y ajuste de líneas por cota de caracteres y ancho en puntos tipográficos.
    /// </summary>
    public static byte[] BuildPdfBytes(
        BabyRecord baby,
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions,
        DateTimeOffset? generatedAtUtc = null)
    {
        var allBodyLines = BuildPdfBodyLines(sleepSessions, feedingSessions);
        var timestamp = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var babyNameEscaped = EscapePdfText(baby.Nombre);

        var (singleLineHeaderFits, wrappedBabyHeaderLines, linesPerPage) = ResolveHeaderLayout(babyNameEscaped);
        var pageChunks = PaginateLines(allBodyLines, linesPerPage);
        var totalPages = pageChunks.Count;

        var contentStreams = new byte[totalPages][];
        for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
        {
            var headerLines = BuildPageHeaderLines(
                baby,
                babyNameEscaped,
                singleLineHeaderFits,
                wrappedBabyHeaderLines,
                timestamp,
                sleepSessions.Count,
                feedingSessions.Count,
                pageIndex + 1,
                totalPages);

            contentStreams[pageIndex] = RenderPageContentStream(headerLines, pageChunks[pageIndex]);
        }

        return AssemblePdfDocument(contentStreams);
    }

    /// <summary>
    /// Sanitiza el nombre del bebé para formar un nombre de archivo seguro sin caracteres especiales.
    /// </summary>
    public static string SanitizeFileName(string input)
    {
        var chars = input.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "bebe" : new string(chars).ToLowerInvariant();
    }

    /// <summary>
    /// Neutraliza comillas, saltos de línea y prefijos de fórmula (=, +, -, @, tab) para evitar
    /// ejecución de fórmulas al abrir el CSV en hojas de cálculo.
    /// </summary>
    internal static string EscapeCsv(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "\"\"";
        }

        var sanitized = input.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
        if (sanitized.Length > 0 && (sanitized[0] is '=' or '+' or '-' or '@' or '\t'))
        {
            sanitized = "'" + sanitized;
        }

        return $"\"{sanitized}\"";
    }

    /// <summary>
    /// Normaliza texto arbitrario al subconjunto seguro para literales PDF en WinAnsiEncoding (ISO-8859-1).
    /// </summary>
    internal static string EscapePdfText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case '(':
                    sb.Append('[');
                    break;
                case ')':
                    sb.Append(']');
                    break;
                case '\\':
                    sb.Append('/');
                    break;
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    if ((ch >= 0x20 && ch <= 0x7E) || (ch >= 0xA0 && ch <= 0xFF))
                    {
                        sb.Append(ch);
                    }
                    else if (ch >= 0x7F)
                    {
                        sb.Append('?');
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static List<string> BuildPdfBodyLines(
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions)
    {
        var allBodyLines = new List<string>();

        foreach (var f in feedingSessions)
        {
            var rawLine = FormatFeedingPdfLine(f);
            allBodyLines.AddRange(WrapPdfLine(EscapePdfText(rawLine), MaxPdfCharsPerLine, MaxPdfWidthPt));
        }

        foreach (var s in sleepSessions)
        {
            var rawLine = FormatSleepPdfLine(s);
            allBodyLines.AddRange(WrapPdfLine(EscapePdfText(rawLine), MaxPdfCharsPerLine, MaxPdfWidthPt));
        }

        if (allBodyLines.Count == 0)
        {
            allBodyLines.Add("Sin sesiones registradas para este perfil.");
        }

        return allBodyLines;
    }

    private static string FormatFeedingPdfLine(FeedingSessionRecord f)
    {
        var alertaStr = f.AlertaTemperatura ? " [ALERTA TEMP]" : "";
        var finalStr = f.MasaFinalG.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{f.MasaFinalG.Value:F1}g")
            : "N/D";
        var volStr = f.MlConsumidos.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{f.MlConsumidos.Value:F1} ml")
            : "N/D";
        var tempStr = f.TempC.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{f.TempC.Value:F1} °C")
            : "N/D";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[Toma] {f.Inicio:yyyy-MM-dd HH:mm} | Inicial: {f.MasaInicialG:F1}g | Final: {finalStr} | Vol: {volStr} | T: {tempStr}{alertaStr}");
    }

    private static string FormatSleepPdfLine(SleepSessionRecord s)
    {
        var movStr = s.MovimientoPromedio.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{s.MovimientoPromedio.Value:F3} kg/m")
            : "N/D";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[Sueño {s.Tipo}] {s.Inicio:yyyy-MM-dd HH:mm} | Mov prom: {movStr} | Interrupciones: {s.Interrupciones}");
    }

    private static (bool SingleLineHeaderFits, IReadOnlyList<string> WrappedBabyLines, int LinesPerPage) ResolveHeaderLayout(
        string babyNameEscaped)
    {
        var probeFullTitle = $"Reporte Histórico Cuna Inteligente (RF32) - Bebé: {babyNameEscaped} (Pág. 999/999)";
        var singleLineHeaderFits = probeFullTitle.Length <= MaxPdfCharsPerLine
            && EstimateHelveticaWidthPt(probeFullTitle) <= MaxPdfWidthPt;

        var wrappedBabyHeaderLines = singleLineHeaderFits
            ? Array.Empty<string>()
            : BoundHeaderLines(
                WrapPdfLine($"Bebé: {babyNameEscaped}", MaxPdfCharsPerLine, MaxPdfWidthPt),
                MaxBabyHeaderLines);

        var headerLineCountEstimate = (singleLineHeaderFits ? 1 : 1 + wrappedBabyHeaderLines.Count) + 3;
        var availableBodyHeightPt = PageTopY - (headerLineCountEstimate * LineHeightPt) - HeaderBodyGapPt - BottomMarginPt;
        var linesPerPage = Math.Max(1, Math.Min(MaxLinesPerPage, (availableBodyHeightPt / LineHeightPt) + 1));

        return (singleLineHeaderFits, wrappedBabyHeaderLines, linesPerPage);
    }

    private static List<List<string>> PaginateLines(List<string> lines, int linesPerPage)
    {
        var pageChunks = new List<List<string>>();
        for (var i = 0; i < lines.Count; i += linesPerPage)
        {
            pageChunks.Add(lines.GetRange(i, Math.Min(linesPerPage, lines.Count - i)));
        }
        return pageChunks;
    }

    private static List<string> BuildPageHeaderLines(
        BabyRecord baby,
        string babyNameEscaped,
        bool singleLineHeaderFits,
        IReadOnlyList<string> wrappedBabyHeaderLines,
        string generatedAt,
        int sleepCount,
        int feedingCount,
        int pageNumber,
        int totalPages)
    {
        var headerLines = new List<string>();
        if (singleLineHeaderFits)
        {
            headerLines.Add($"Reporte Histórico Cuna Inteligente (RF32) - Bebé: {babyNameEscaped} (Pág. {pageNumber}/{totalPages})");
        }
        else
        {
            headerLines.Add($"Reporte Histórico Cuna Inteligente (RF32) - Pág. {pageNumber}/{totalPages}");
            headerLines.AddRange(wrappedBabyHeaderLines);
        }

        headerLines.Add(EscapePdfText($"Fecha de nacimiento: {baby.FechaNacimiento:yyyy-MM-dd} | Generado: {generatedAt} UTC"));
        headerLines.Add(EscapePdfText($"Sesiones de sueño: {sleepCount} | Sesiones de alimentación: {feedingCount}"));
        headerLines.Add("------------------------------------------------------------------------");
        return headerLines;
    }

    private static byte[] RenderPageContentStream(IReadOnlyList<string> headerLines, IReadOnlyList<string> bodyLines)
    {
        var cb = new StringBuilder();
        cb.Append("BT\n/F1 10 Tf\n");

        var y = PageTopY;
        foreach (var h in headerLines)
        {
            cb.Append($"1 0 0 1 50 {y} Tm ({h}) Tj\n");
            y -= LineHeightPt;
        }

        // Separación vertical adicional tras el encabezado antes de la primera fila de datos
        y -= HeaderBodyGapPt;

        foreach (var row in bodyLines)
        {
            cb.Append($"1 0 0 1 50 {y} Tm ({row}) Tj\n");
            y -= LineHeightPt;
        }

        cb.Append("ET\n");
        return Encoding.Latin1.GetBytes(cb.ToString());
    }

    private static byte[] AssemblePdfDocument(byte[][] contentStreams)
    {
        // Objetos PDF: 1=Catalog, 2=Pages, 3=Font Helvetica, luego por página p: Page(4+p*2) y Contents(5+p*2)
        var totalPages = contentStreams.Length;
        var pageObjIds = new int[totalPages];
        for (var p = 0; p < totalPages; p++)
        {
            pageObjIds[p] = 4 + (p * 2);
        }

        var totalObjects = 3 + (totalPages * 2);
        var offsets = new long[totalObjects + 1];

        using var ms = new MemoryStream();
        void WriteAscii(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.4\n");

        offsets[1] = ms.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        var kidsList = string.Join(" ", pageObjIds.Select(id => $"{id} 0 R"));
        WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{kidsList}] /Count {totalPages} >>\nendobj\n");

        offsets[3] = ms.Position;
        WriteAscii("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (var p = 0; p < totalPages; p++)
        {
            var pageId = 4 + (p * 2);
            var contentId = 5 + (p * 2);

            offsets[pageId] = ms.Position;
            WriteAscii(
                $"{pageId} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R >> >> >>\nendobj\n");

            offsets[contentId] = ms.Position;
            WriteAscii($"{contentId} 0 obj\n<< /Length {contentStreams[p].Length} >>\nstream\n");
            ms.Write(contentStreams[p], 0, contentStreams[p].Length);
            WriteAscii("endstream\nendobj\n");
        }

        var xrefOffset = ms.Position;
        WriteAscii($"xref\n0 {totalObjects + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        for (var objId = 1; objId <= totalObjects; objId++)
        {
            WriteAscii($"{offsets[objId]:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {totalObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static IReadOnlyList<string> BoundHeaderLines(IReadOnlyList<string> lines, int maxLines)
    {
        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var bounded = new List<string>(maxLines);
        for (var i = 0; i < maxLines; i++)
        {
            bounded.Add(lines[i]);
        }

        var last = bounded[maxLines - 1];
        bounded[maxLines - 1] = last.Length > 3 ? last[..^3].TrimEnd() + "..." : "...";
        return bounded;
    }

    /// <summary>
    /// Ajusta una línea ya escapada respetando tanto el tope de caracteres como el ancho acumulado
    /// estimado de Helvetica a 10 pt para no desbordar la caja MediaBox Letter (612 pt).
    /// </summary>
    internal static IReadOnlyList<string> WrapPdfLine(string escapedText, int maxChars, float maxWidthPt)
    {
        if (string.IsNullOrEmpty(escapedText))
        {
            return new[] { string.Empty };
        }

        if (escapedText.Length <= maxChars && EstimateHelveticaWidthPt(escapedText) <= maxWidthPt)
        {
            return new[] { escapedText };
        }

        var lines = new List<string>();
        var remaining = escapedText.AsSpan();
        var isFirst = true;

        while (remaining.Length > 0)
        {
            var prefix = isFirst ? string.Empty : "  ";
            var prefixWidth = EstimateHelveticaWidthPt(prefix);
            var charLimit = Math.Max(8, maxChars - prefix.Length);
            var widthLimit = Math.Max(80f, maxWidthPt - prefixWidth);

            var fitCount = CountFittingChars(remaining, charLimit, widthLimit);
            if (fitCount >= remaining.Length)
            {
                lines.Add(prefix + remaining.ToString());
                break;
            }

            var slice = remaining[..fitCount];
            var breakIdx = slice.LastIndexOf(' ');
            if (breakIdx < fitCount / 3 || breakIdx <= 0)
            {
                breakIdx = fitCount;
            }

            lines.Add(prefix + remaining[..breakIdx].TrimEnd().ToString());
            remaining = remaining[breakIdx..].TrimStart();
            isFirst = false;
        }

        return lines;
    }

    private static int CountFittingChars(ReadOnlySpan<char> text, int charLimit, float widthLimit)
    {
        var fitCount = 0;
        var accWidth = 0f;

        while (fitCount < text.Length && fitCount < charLimit)
        {
            var charWidth = GetHelveticaCharWidthPt(text[fitCount]);
            if (fitCount > 0 && accWidth + charWidth > widthLimit)
            {
                break;
            }
            accWidth += charWidth;
            fitCount++;
        }

        return Math.Max(1, fitCount);
    }

    internal static float EstimateHelveticaWidthPt(string text, float fontSize = DefaultFontSizePt)
    {
        var total = 0f;
        foreach (var ch in text)
        {
            total += GetHelveticaCharWidthPt(ch, fontSize);
        }
        return total;
    }

    /// <summary>
    /// Tabla de métricas AFM estándar de Helvetica/WinAnsiEncoding en milésimas de em.
    /// </summary>
    internal static float GetHelveticaCharWidthPt(char ch, float fontSize = DefaultFontSizePt)
    {
        var thousandths = ch switch
        {
            ' ' => 278,
            '!' => 278,
            '"' => 355,
            '#' or '$' => 556,
            '%' => 889,
            '&' => 667,
            '\'' => 191,
            '(' or ')' => 333,
            '*' => 389,
            '+' => 584,
            ',' => 278,
            '-' => 333,
            '.' or '/' => 278,
            >= '0' and <= '9' => 556,
            ':' or ';' => 278,
            '<' or '=' or '>' => 584,
            '?' => 556,
            '@' => 1015,
            'A' or 'B' => 667,
            'C' or 'D' => 722,
            'E' => 667,
            'F' => 611,
            'G' => 778,
            'H' => 722,
            'I' => 278,
            'J' => 500,
            'K' => 667,
            'L' => 556,
            'M' => 833,
            'N' => 722,
            'O' => 778,
            'P' => 667,
            'Q' => 778,
            'R' => 722,
            'S' => 667,
            'T' => 611,
            'U' => 722,
            'V' => 667,
            'W' => 944,
            'X' or 'Y' => 667,
            'Z' => 611,
            '[' or ']' => 278,
            '_' => 556,
            '`' => 333,
            'a' or 'b' => 556,
            'c' => 500,
            'd' or 'e' => 556,
            'f' => 278,
            'g' or 'h' => 556,
            'i' or 'j' => 222,
            'k' => 500,
            'l' => 222,
            'm' => 833,
            'n' or 'o' or 'p' or 'q' => 556,
            'r' => 333,
            's' => 500,
            't' => 278,
            'u' => 556,
            'v' => 500,
            'w' => 722,
            'x' or 'y' or 'z' => 500,
            '{' or '}' => 334,
            '|' => 260,
            '~' => 584,
            '°' => 400,
            '¡' => 333,
            '¿' => 611,
            'Á' or 'É' or 'Ü' => 667,
            'Í' => 278,
            'Ó' => 778,
            'Ú' or 'Ñ' => 722,
            'á' or 'é' or 'ó' or 'ú' or 'ñ' or 'ü' => 556,
            'í' => 278,
            _ => 778
        };

        return thousandths * fontSize / 1000f;
    }
}
