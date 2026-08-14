using System.IO;
using System.Text.Json;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public sealed partial class OpenAiExtractionService
{
    private static string BuildInputList(DocumentImageSet document)
    {
        if (document.PdfPath is not null)
        {
            return $"- PDF: {Path.GetFileName(document.PdfPath)}";
        }

        return string.Join(
            Environment.NewLine,
            document.ImagePaths.Select((path, index) => $"- Page/image {index + 1}: {Path.GetFileName(path)}"));
    }

    private static string NormalizeInputDetail(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "low" or "high" or "auto"
            ? normalized
            : throw new InvalidOperationException("Visual detail must be Low, High, or Auto.");
    }

    private static void EnsureImagePayloadIsSupported(IReadOnlyList<string> imagePaths)
    {
        if (imagePaths.Count == 0)
        {
            throw new InvalidOperationException("The image document has no input files.");
        }

        if (imagePaths.Count > MaxImageInputs)
        {
            throw new InvalidOperationException($"A document can contain at most {MaxImageInputs:N0} image inputs.");
        }

    }

    private static void EnsurePdfSizeIsSupported(string pdfPath)
    {
        var fileInfo = new FileInfo(pdfPath);
        if (fileInfo.Length >= MaxPdfBytes)
        {
            throw new InvalidOperationException($"PDF must be smaller than 50 MB: {pdfPath}");
        }
    }

    private static string FileToBase64(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var bytes = new byte[checked((int)stream.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(1024 * 1024, bytes.Length - offset));
            if (read == 0)
            {
                throw new EndOfStreamException($"PDF ended before it could be read completely: {filePath}");
            }

            offset += read;
        }

        return $"data:application/pdf;base64,{Convert.ToBase64String(bytes)}";
    }

    private static void EnsureEmptyValues(ExtractionResult result, IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            result.Values.TryAdd(field, null);
        }
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? JsonElementToCellValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "TRUE",
            JsonValueKind.False => "FALSE",
            _ => value.GetRawText()
        };
    }

}
