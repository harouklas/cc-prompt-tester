using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Xml;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public static class ExcelReportWriter
{
    public static void ValidateTarget(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new InvalidOperationException("Choose where to save the Excel report.");
        }

        var outputFolder = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            throw new InvalidOperationException("The Excel report must have a valid destination folder.");
        }

        Directory.CreateDirectory(outputFolder);
        if (File.Exists(reportPath))
        {
            using var existing = new FileStream(reportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return;
        }

        var probePath = Path.Combine(outputFolder, $".prompt-tester-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    public static string Write(IReadOnlyList<ExtractionResult> results, IReadOnlyList<string> fields, string reportPath)
    {
        var outputFolder = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        var headers = new List<string>
        {
            "Document Name",
            "Document Path",
            "Source Type",
            "Input Count",
            "Input Tokens",
            "Cached Input Tokens",
            "Cache Write Tokens",
            "Output Tokens",
            "Reasoning Tokens",
            "Total Tokens",
            "Input Cost USD",
            "Output Cost USD",
            "Total Cost USD"
        };

        foreach (var field in fields)
        {
            AddUniqueHeader(headers, ToHeaderLabel(field));
        }

        AddUniqueHeader(headers, "Status");
        AddUniqueHeader(headers, "Error");
        AddUniqueHeader(headers, "Model");
        AddUniqueHeader(headers, "Response ID");
        AddUniqueHeader(headers, "API Status");
        AddUniqueHeader(headers, "Pricing Tier");
        AddUniqueHeader(headers, "Decision Summary");
        AddUniqueHeader(headers, "Decision Log");
        AddUniqueHeader(headers, "Decision Log Error");
        AddUniqueHeader(headers, "Processing Seconds");

        var temporaryPath = Path.Combine(outputFolder ?? "", $".{Path.GetFileName(reportPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "[Content_Types].xml", ContentTypesXml);
                AddEntry(archive, "_rels/.rels", PackageRelsXml);
                AddEntry(archive, "xl/workbook.xml", WorkbookXml);
                AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
                AddEntry(archive, "xl/styles.xml", StylesXml);
                AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(headers, results, fields));
            }

            using (var validationArchive = ZipFile.OpenRead(temporaryPath))
            {
                if (validationArchive.GetEntry("xl/worksheets/sheet1.xml") is null)
                {
                    throw new InvalidOperationException("The generated Excel workbook failed validation.");
                }
            }

            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return reportPath;
    }

    private static string BuildWorksheetXml(IReadOnlyList<string> headers, IReadOnlyList<ExtractionResult> results, IReadOnlyList<string> fields)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = true,
            Indent = false
        });

        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView");
        writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane");
        writer.WriteAttributeString("xSplit", "2");
        writer.WriteAttributeString("ySplit", "1");
        writer.WriteAttributeString("topLeftCell", "C2");
        writer.WriteAttributeString("activePane", "bottomRight");
        writer.WriteAttributeString("state", "frozen");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        WriteColumns(writer, headers);
        writer.WriteStartElement("sheetData");
        WriteRow(writer, 1, headers.Select(value => CellValue.Text(value)).ToList(), isHeader: true);

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var row = new List<CellValue>
            {
                CellValue.Text(result.DocumentName),
                CellValue.Text(result.DocumentPath),
                CellValue.Text(result.SourceType),
                CellValue.Number(result.ImageCount),
                CellValue.Number(result.InputTokens),
                CellValue.Number(result.CachedInputTokens),
                CellValue.Number(result.CacheWriteTokens),
                CellValue.Number(result.OutputTokens),
                CellValue.Number(result.ReasoningTokens),
                CellValue.Number(result.TotalTokens),
                CellValue.Cost(result.HasCostEstimate ? result.InputCostUsd : null),
                CellValue.Cost(result.HasCostEstimate ? result.OutputCostUsd : null),
                CellValue.Cost(result.HasCostEstimate ? result.TotalCostUsd : null)
            };
            row.AddRange(fields.Select(field => CellValue.Text(result.Values.TryGetValue(field, out var value) ? value : null)));
            row.Add(CellValue.Text(result.Status));
            row.Add(CellValue.Text(result.Error));
            row.Add(CellValue.Text(result.Model));
            row.Add(CellValue.Text(result.ResponseId));
            row.Add(CellValue.Text(result.CompletionStatus));
            row.Add(CellValue.Text(result.PricingLabel));
            row.Add(CellValue.Text(result.DecisionSummary));
            row.Add(CellValue.Text(result.DecisionLogPath));
            row.Add(CellValue.Text(result.DecisionLogError));
            row.Add(CellValue.Seconds(result.ProcessingSeconds));

            WriteRow(writer, index + 2, row, isHeader: false);
        }

        writer.WriteEndElement();
        writer.WriteStartElement("autoFilter");
        writer.WriteAttributeString("ref", $"A1:{ColumnName(headers.Count)}{Math.Max(results.Count + 1, 1)}");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();

        return builder.ToString();
    }

    private static void WriteColumns(XmlWriter writer, IReadOnlyList<string> headers)
    {
        writer.WriteStartElement("cols");
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            var width = header switch
            {
                "Document Name" => 24,
                "Document Path" or "Decision Log" => 48,
                "Error" or "Decision Summary" or "Decision Log Error" => 42,
                "Response ID" => 32,
                _ when header.EndsWith("Tokens", StringComparison.OrdinalIgnoreCase) => 18,
                _ when header.Contains("Cost", StringComparison.OrdinalIgnoreCase) => 18,
                _ => 20
            };

            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteRow(XmlWriter writer, int rowNumber, IReadOnlyList<CellValue> values, bool isHeader)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowNumber.ToString());

        for (var index = 0; index < values.Count; index++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{ColumnName(index + 1)}{rowNumber}");
            if (isHeader)
            {
                writer.WriteAttributeString("s", "1");
            }
            else if (values[index].StyleIndex > 0)
            {
                writer.WriteAttributeString("s", values[index].StyleIndex.ToString(CultureInfo.InvariantCulture));
            }

            if (values[index].IsNumber && !string.IsNullOrWhiteSpace(values[index].Value))
            {
                writer.WriteAttributeString("t", "n");
                writer.WriteElementString("v", values[index].Value);
            }
            else
            {
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is");
                writer.WriteStartElement("t");
                writer.WriteAttributeString("xml", "space", null, "preserve");
                writer.WriteString(RemoveInvalidXmlCharacters(values[index].Value ?? ""));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string ColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var columnName = "";

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }

    private static string ToHeaderLabel(string value)
    {
        var label = string.Join(
            " ",
            value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())));
        return string.IsNullOrWhiteSpace(label) ? value : label;
    }

    private static void AddUniqueHeader(ICollection<string> headers, string preferredHeader)
    {
        var header = preferredHeader;
        var suffix = 2;
        while (headers.Contains(header, StringComparer.OrdinalIgnoreCase))
        {
            header = $"{preferredHeader} {suffix}";
            suffix++;
        }

        headers.Add(header);
    }

    private static string RemoveInvalidXmlCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string? LimitTextForExcel(string? value)
    {
        const int maxCellCharacters = 32_767;
        const string truncationNotice = "\n[truncated for Excel; see the per-document decision log for full details]";
        if (value is null || value.Length <= maxCellCharacters)
        {
            return value;
        }

        var retainedLength = maxCellCharacters - truncationNotice.Length;
        if (retainedLength > 0 && char.IsHighSurrogate(value[retainedLength - 1]))
        {
            retainedLength--;
        }

        return value[..retainedLength] + truncationNotice;
    }

    private sealed record CellValue(string? Value, bool IsNumber, int StyleIndex)
    {
        public static CellValue Text(string? value) => new(LimitTextForExcel(value), false, 0);

        public static CellValue Number(int value) => new(value.ToString(CultureInfo.InvariantCulture), true, 0);

        public static CellValue Cost(decimal? value) => new(
            value?.ToString("0.00000000", CultureInfo.InvariantCulture),
            true,
            2);

        public static CellValue Seconds(double value) => new(
            value.ToString("0.00", CultureInfo.InvariantCulture),
            true,
            3);
    }

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string PackageRelsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Extracted Values" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private const string WorkbookRelsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="2">
            <numFmt numFmtId="164" formatCode="0.00000000"/>
            <numFmt numFmtId="165" formatCode="0.00"/>
          </numFmts>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF123A6D"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFDCE3ED"/></left><right style="thin"><color rgb="FFDCE3ED"/></right><top style="thin"><color rgb="FFDCE3ED"/></top><bottom style="thin"><color rgb="FFDCE3ED"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="4">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
          </cellXfs>
        </styleSheet>
        """;
}
