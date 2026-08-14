using System.Text.Json;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public sealed partial class OpenAiExtractionService
{
    private static ExtractionResult MergeChunkResults(
        DocumentImageSet document,
        IReadOnlyList<string> fields,
        ModelDefinition model,
        int plannedChunkCount,
        IReadOnlyList<ExtractionResult> chunks,
        TimeSpan elapsed)
    {
        var failed = chunks.Where(chunk => !string.Equals(chunk.Status, "success", StringComparison.OrdinalIgnoreCase)).ToArray();
        var result = new ExtractionResult
        {
            DocumentName = document.DocumentName,
            DocumentPath = document.DocumentPath,
            SourceType = document.SourceType,
            ImageCount = document.InputCount,
            Model = model.Id,
            Status = failed.Length == 0 && chunks.Count == plannedChunkCount ? "success" : "error",
            CompletionStatus = failed.Length == 0 && chunks.Count == plannedChunkCount ? "completed" : "partial_error",
            StopBatch = chunks.Any(chunk => chunk.StopBatch),
            Error = string.Join(" | ", failed.Select(chunk => $"{chunk.DocumentName}: {chunk.Error}").Where(value => !string.IsNullOrWhiteSpace(value))),
            ResponseId = string.Join(", ", chunks.Select(chunk => chunk.ResponseId).Where(value => !string.IsNullOrWhiteSpace(value))),
            InputTokens = chunks.Sum(chunk => chunk.InputTokens),
            CachedInputTokens = chunks.Sum(chunk => chunk.CachedInputTokens),
            CacheWriteTokens = chunks.Sum(chunk => chunk.CacheWriteTokens),
            OutputTokens = chunks.Sum(chunk => chunk.OutputTokens),
            ReasoningTokens = chunks.Sum(chunk => chunk.ReasoningTokens),
            TotalTokens = chunks.Sum(chunk => chunk.TotalTokens),
            InputCostUsd = chunks.Sum(chunk => chunk.InputCostUsd),
            OutputCostUsd = chunks.Sum(chunk => chunk.OutputCostUsd),
            TotalCostUsd = chunks.Sum(chunk => chunk.TotalCostUsd),
            HasCostEstimate = chunks.All(chunk => chunk.HasCostEstimate),
            UsedLongContextPricing = chunks.Any(chunk => chunk.UsedLongContextPricing),
            PricingLabel = string.Join(" + ", chunks.Select(chunk => chunk.PricingLabel).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
            ProcessingSeconds = Math.Round(elapsed.TotalSeconds, 2),
            DecisionSummary = string.Join(Environment.NewLine, chunks.Select(chunk => chunk.DecisionSummary).Where(value => !string.IsNullOrWhiteSpace(value))),
            ModelReasoningSummary = string.Join(Environment.NewLine + Environment.NewLine, chunks.Select(chunk => chunk.ModelReasoningSummary).Where(value => !string.IsNullOrWhiteSpace(value)))
        };

        foreach (var field in fields)
        {
            var mergedValue = MergeFieldValues(chunks.Select(chunk => chunk.Values.GetValueOrDefault(field)));
            result.Values[field] = mergedValue;
            var decision = MergeFieldDecisions(
                chunks.Select(chunk => chunk.FieldDecisions.GetValueOrDefault(field)),
                hasValue: !string.IsNullOrWhiteSpace(mergedValue));
            if (decision is not null)
            {
                result.FieldDecisions[field] = decision;
            }
        }

        foreach (var warning in chunks.SelectMany(chunk => chunk.Warnings).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result.Warnings.Add(warning);
        }

        result.Warnings.Add(
            $"Auto visual detail was preserved. This {document.InputCount}-image document was processed in {chunks.Count} page chunks (maximum {MaxAutoImagesPerRequest} images each) to avoid long-context requests.");
        if (chunks.Count < plannedChunkCount)
        {
            result.Warnings.Add($"Processing stopped after {chunks.Count} of {plannedChunkCount} planned chunks because a blocking API error occurred.");
        }

        return result;
    }

    private static string? MergeFieldValues(IEnumerable<string?> rawValues)
    {
        var values = new List<string>();
        foreach (var rawValue in rawValues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var value = rawValue!;
            try
            {
                using var json = JsonDocument.Parse(value);
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(json.RootElement.EnumerateArray()
                        .Where(item => item.ValueKind != JsonValueKind.Null)
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ToString()));
                    continue;
                }
            }
            catch (JsonException)
            {
                // Scalar spreadsheet values are not JSON and are retained below.
            }

            values.Add(value);
        }

        var distinct = values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return distinct.Length switch
        {
            0 => null,
            1 => distinct[0],
            _ => JsonSerializer.Serialize(distinct)
        };
    }

    private static FieldDecision? MergeFieldDecisions(
        IEnumerable<FieldDecision?> rawDecisions,
        bool hasValue)
    {
        var decisions = rawDecisions.Where(decision => decision is not null).Cast<FieldDecision>().ToArray();
        if (decisions.Length == 0)
        {
            return null;
        }

        var valueDecisions = decisions.Where(decision =>
            !string.Equals(decision.Status, "missing", StringComparison.OrdinalIgnoreCase)).ToArray();
        var relevant = hasValue && valueDecisions.Length > 0 ? valueDecisions : decisions;

        return new FieldDecision
        {
            Status = ResolveMergedDecisionStatus(relevant, hasValue),
            Evidence = MergeDecisionText(relevant.Select(decision => decision.Evidence)),
            Explanation = MergeDecisionText(relevant.Select(decision => decision.Explanation)) ?? "",
            Confidence = ResolveLowestConfidence(relevant)
        };
    }

    private static string ResolveMergedDecisionStatus(IReadOnlyList<FieldDecision> decisions, bool hasValue)
    {
        foreach (var status in new[] { "conflicting", "ambiguous", "extracted" })
        {
            if (decisions.Any(decision => string.Equals(decision.Status, status, StringComparison.OrdinalIgnoreCase)))
            {
                return status;
            }
        }

        return hasValue ? "ambiguous" : "missing";
    }

    private static string? MergeDecisionText(IEnumerable<string?> rawValues)
    {
        var values = rawValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" | ", values);
    }

    private static string ResolveLowestConfidence(IEnumerable<FieldDecision> decisions)
    {
        var confidence = decisions.Select(decision => decision.Confidence).ToArray();
        if (confidence.Any(value => string.Equals(value, "low", StringComparison.OrdinalIgnoreCase)))
        {
            return "low";
        }

        if (confidence.Any(value => string.Equals(value, "medium", StringComparison.OrdinalIgnoreCase)))
        {
            return "medium";
        }

        return confidence.Any(value => string.Equals(value, "high", StringComparison.OrdinalIgnoreCase))
            ? "high"
            : "low";
    }

}
