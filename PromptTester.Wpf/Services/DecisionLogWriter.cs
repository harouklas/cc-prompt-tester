using System.Globalization;
using System.IO;
using System.Text;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public static class DecisionLogWriter
{
    public static string CreateRunFolder(string reportPath)
    {
        var reportFolder = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(reportFolder))
        {
            throw new InvalidOperationException("The Excel report must have a valid destination folder.");
        }

        Directory.CreateDirectory(reportFolder);
        var reportName = SanitizeFileName(Path.GetFileNameWithoutExtension(reportPath));
        var baseFolderName = $"{reportName}_decision_logs_{DateTime.Now:yyyyMMdd_HHmmss}";
        var candidate = Path.Combine(reportFolder, baseFolderName);
        var suffix = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(reportFolder, $"{baseFolderName}_{suffix++}");
        }

        Directory.CreateDirectory(candidate);
        var probePath = Path.Combine(candidate, $".write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probePath, "ok", Encoding.UTF8);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }

        return candidate;
    }

    public static string Write(
        string runFolder,
        int documentNumber,
        ExtractionResult result,
        IReadOnlyList<string> fields,
        string extractionPrompt)
    {
        Directory.CreateDirectory(runFolder);
        var documentName = SanitizeFileName(result.DocumentName);
        var logPath = Path.Combine(runFolder, $"{documentNumber:D4}_{documentName}.log");
        var temporaryPath = $"{logPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, BuildContent(result, fields, extractionPrompt), new UTF8Encoding(false));
            File.Move(temporaryPath, logPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return logPath;
    }

    private static string BuildContent(
        ExtractionResult result,
        IReadOnlyList<string> fields,
        string extractionPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PROMPT TESTER - DOCUMENT DECISION LOG");
        builder.AppendLine(new string('=', 78));
        builder.AppendLine("This file contains an audit-friendly decision rationale and, when available,");
        builder.AppendLine("an official reasoning summary. It does not contain hidden chain-of-thought.");
        builder.AppendLine("Decision logs can contain sensitive document data; store them accordingly.");
        builder.AppendLine();
        Append(builder, "Generated (UTC)", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Append(builder, "Document", result.DocumentName);
        Append(builder, "Source type", result.SourceType);
        Append(builder, "Source path", result.DocumentPath);
        Append(builder, "Input count", result.ImageCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, "Model", result.Model);
        Append(builder, "Response ID", result.ResponseId);
        Append(builder, "API completion", result.CompletionStatus);
        Append(builder, "Extraction status", result.Status);
        Append(builder, "Processing seconds", result.ProcessingSeconds.ToString("0.##", CultureInfo.InvariantCulture));
        builder.AppendLine();

        builder.AppendLine("USAGE AND ESTIMATED STANDARD API COST");
        builder.AppendLine(new string('-', 78));
        Append(builder, "Input tokens", result.InputTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Cached input tokens", result.CachedInputTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Cache-write tokens", result.CacheWriteTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Output tokens", result.OutputTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Reasoning tokens (included in output)", result.ReasoningTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Total tokens", result.TotalTokens.ToString("N0", CultureInfo.InvariantCulture));
        Append(builder, "Pricing tier", result.PricingLabel);
        Append(builder, "Estimated input cost (USD)", result.HasCostEstimate ? result.InputCostUsd.ToString("0.00000000", CultureInfo.InvariantCulture) : "Unavailable");
        Append(builder, "Estimated output cost (USD)", result.HasCostEstimate ? result.OutputCostUsd.ToString("0.00000000", CultureInfo.InvariantCulture) : "Unavailable");
        Append(builder, "Estimated total cost (USD)", result.HasCostEstimate ? result.TotalCostUsd.ToString("0.00000000", CultureInfo.InvariantCulture) : "Unavailable");
        builder.AppendLine();

        builder.AppendLine("EXTRACTED VALUES");
        builder.AppendLine(new string('-', 78));
        foreach (var field in fields)
        {
            result.Values.TryGetValue(field, out var value);
            Append(builder, field, value ?? "<null>");
        }

        builder.AppendLine();
        builder.AppendLine("DECISION RATIONALE");
        builder.AppendLine(new string('-', 78));
        builder.AppendLine(string.IsNullOrWhiteSpace(result.DecisionSummary)
            ? "No model-authored decision summary was returned."
            : result.DecisionSummary.Trim());

        foreach (var field in fields)
        {
            builder.AppendLine();
            builder.AppendLine($"[{field}]");
            if (!result.FieldDecisions.TryGetValue(field, out var decision))
            {
                builder.AppendLine("  No field-level rationale was returned.");
                continue;
            }

            Append(builder, "  Decision", decision.Status);
            Append(builder, "  Confidence", decision.Confidence);
            Append(builder, "  Evidence", decision.Evidence ?? "<none reported>");
            Append(builder, "  Explanation", decision.Explanation);
        }

        builder.AppendLine();
        builder.AppendLine("OFFICIAL REASONING SUMMARY (WHEN SUPPORTED AND AVAILABLE)");
        builder.AppendLine(new string('-', 78));
        builder.AppendLine(string.IsNullOrWhiteSpace(result.ModelReasoningSummary)
            ? "Not available for this response/model/account."
            : result.ModelReasoningSummary.Trim());

        builder.AppendLine();
        builder.AppendLine("WARNINGS AND ERRORS");
        builder.AppendLine(new string('-', 78));
        if (result.Warnings.Count == 0 && string.IsNullOrWhiteSpace(result.Error))
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                builder.AppendLine($"WARNING: {warning}");
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                builder.AppendLine($"ERROR: {result.Error}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("EXTRACTION PROMPT SNAPSHOT");
        builder.AppendLine(new string('-', 78));
        builder.AppendLine(extractionPrompt.Trim());
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, string? value)
    {
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(string.IsNullOrWhiteSpace(value) ? "<not available>" : value.Trim());
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "document" : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidCharacter, '-');
        }

        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0)
        {
            sanitized = "document";
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80].TrimEnd();
    }
}
