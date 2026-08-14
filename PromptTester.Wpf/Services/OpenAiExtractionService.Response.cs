using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public sealed partial class OpenAiExtractionService
{
    private static async Task<ApiResponse> SendRequestAsync(
        string requestJson,
        string apiKey,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode || attempt == maxAttempts || !IsTransient(response.StatusCode))
            {
                return new ApiResponse(response.StatusCode, responseJson, attempt);
            }

            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
        }

        throw new InvalidOperationException("The OpenAI API request did not produce a response.");
    }

    private static void ApplySuccessfulResponse(
        string responseJson,
        ExtractionResult result,
        IReadOnlyList<string> fields,
        ModelDefinition model)
    {
        using var responseDocument = JsonDocument.Parse(responseJson);
        var root = responseDocument.RootElement;
        result.ResponseId = GetStringProperty(root, "id") ?? "";
        result.CompletionStatus = GetStringProperty(root, "status") ?? "unknown";
        ApplyTokenUsage(root, result, model);
        result.ModelReasoningSummary = ExtractReasoningSummary(root);

        if (!string.Equals(result.CompletionStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = root.TryGetProperty("incomplete_details", out var incompleteDetails)
                ? GetStringProperty(incompleteDetails, "reason")
                : null;
            throw new InvalidOperationException($"The API response was {result.CompletionStatus}{(string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}")}");
        }

        var refusal = ExtractRefusal(root);
        if (!string.IsNullOrWhiteSpace(refusal))
        {
            throw new InvalidOperationException($"The model refused this document: {refusal}");
        }

        ApplyStructuredOutput(ExtractOutputText(root), result, fields);
    }

    private static void ApplyTokenUsage(JsonElement root, ExtractionResult result, ModelDefinition model)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return;
        }

        result.InputTokens = GetIntProperty(usage, "input_tokens");
        result.OutputTokens = GetIntProperty(usage, "output_tokens");
        result.TotalTokens = GetIntProperty(usage, "total_tokens");
        if (result.TotalTokens == 0)
        {
            result.TotalTokens = result.InputTokens + result.OutputTokens;
        }

        if (usage.TryGetProperty("input_tokens_details", out var inputDetails))
        {
            result.CachedInputTokens = GetIntProperty(inputDetails, "cached_tokens");
            result.CacheWriteTokens = GetIntProperty(inputDetails, "cache_write_tokens");
        }

        if (usage.TryGetProperty("output_tokens_details", out var outputDetails))
        {
            result.ReasoningTokens = GetIntProperty(outputDetails, "reasoning_tokens");
        }

        var cost = ModelCatalog.CalculateCost(
            model,
            result.InputTokens,
            result.CachedInputTokens,
            result.CacheWriteTokens,
            result.OutputTokens);
        result.InputCostUsd = cost.InputCostUsd;
        result.OutputCostUsd = cost.OutputCostUsd;
        result.TotalCostUsd = cost.TotalCostUsd;
        result.UsedLongContextPricing = cost.UsedLongContextPricing;
        result.PricingLabel = cost.PricingLabel;
        result.HasCostEstimate = true;
    }

    private static void ApplyStructuredOutput(
        string outputText,
        ExtractionResult result,
        IReadOnlyList<string> fields)
    {
        using var outputDocument = JsonDocument.Parse(outputText);
        var root = outputDocument.RootElement;
        var values = root.TryGetProperty("values", out var valuesElement) ? valuesElement : root;

        foreach (var field in fields)
        {
            result.Values[field] = TryGetProperty(values, field, out var value)
                ? JsonElementToCellValue(value)
                : null;
        }

        if (!root.TryGetProperty("decision_rationale", out var rationale))
        {
            if (fields.Count <= MaxDetailedAuditFields)
            {
                result.Warnings.Add("The response did not contain the requested decision rationale.");
            }
            return;
        }

        result.DecisionSummary = GetStringProperty(rationale, "summary") ?? "";
        if (rationale.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            foreach (var warning in warnings.EnumerateArray())
            {
                if (warning.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(warning.GetString()))
                {
                    result.Warnings.Add(warning.GetString()!);
                }
            }
        }

        if (!rationale.TryGetProperty("field_decisions", out var decisions))
        {
            return;
        }

        foreach (var field in fields)
        {
            if (!TryGetProperty(decisions, field, out var decision) || decision.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.FieldDecisions[field] = new FieldDecision
            {
                Status = GetStringProperty(decision, "status") ?? "",
                Evidence = GetStringProperty(decision, "evidence"),
                Explanation = GetStringProperty(decision, "explanation") ?? "",
                Confidence = GetStringProperty(decision, "confidence") ?? ""
            };
        }
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            return directText.GetString() ?? "";
        }

        if (!root.TryGetProperty("output", out var outputItems) || outputItems.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The API response did not include output text.");
        }

        foreach (var outputItem in outputItems.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentItems) || contentItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentItems.EnumerateArray())
            {
                if (GetStringProperty(contentItem, "type") == "output_text"
                    && contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? "";
                }
            }
        }

        throw new InvalidOperationException("The model did not return a structured extraction result.");
    }

    private static string ExtractReasoningSummary(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var outputItems) || outputItems.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var summaries = new List<string>();
        foreach (var outputItem in outputItems.EnumerateArray())
        {
            if (GetStringProperty(outputItem, "type") != "reasoning"
                || !outputItem.TryGetProperty("summary", out var summaryItems)
                || summaryItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var summaryItem in summaryItems.EnumerateArray())
            {
                if (GetStringProperty(summaryItem, "type") == "summary_text"
                    && summaryItem.TryGetProperty("text", out var text))
                {
                    var value = text.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        summaries.Add(value.Trim());
                    }
                }
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, summaries);
    }

    private static string ExtractRefusal(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var outputItems) || outputItems.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var outputItem in outputItems.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentItems) || contentItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentItems.EnumerateArray())
            {
                if (GetStringProperty(contentItem, "type") == "refusal")
                {
                    return GetStringProperty(contentItem, "refusal")
                        ?? GetStringProperty(contentItem, "text")
                        ?? "Refused without an explanation.";
                }
            }
        }

        return "";
    }

    private static ApiError ExtractApiError(string responseJson, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return new ApiError(
                    GetStringProperty(error, "message") ?? fallback,
                    GetStringProperty(error, "code"),
                    GetStringProperty(error, "param"));
            }
        }
        catch (JsonException)
        {
            // Use the HTTP status fallback below when the API did not return JSON.
        }

        return new ApiError(fallback, null, null);
    }

    private static bool ShouldStopBatch(HttpStatusCode statusCode, ApiError error)
    {
        if (statusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500)
        {
            return true;
        }

        if (statusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(error.Param)
            && error.Param.StartsWith("input", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var message = error.Message;
        if ((message.Contains("image", StringComparison.OrdinalIgnoreCase)
             || message.Contains("pdf", StringComparison.OrdinalIgnoreCase)
             || message.Contains("file", StringComparison.OrdinalIgnoreCase))
            && !message.Contains("schema", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A non-document-specific 400 usually reflects a model, schema, or request
        // configuration problem and would fail identically for every remaining file.
        return true;
    }

    private static bool IsReasoningSummaryAvailabilityError(ApiError error)
    {
        return (!string.IsNullOrWhiteSpace(error.Param)
                && error.Param.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
            || error.Message.Contains("reasoning summary", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("organization verification", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("organization must be verified", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("summary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requestedDelay = retryAfter?.Delta;
        if (requestedDelay is null && retryAfter?.Date is DateTimeOffset retryDate)
        {
            requestedDelay = retryDate - DateTimeOffset.UtcNow;
        }

        requestedDelay ??= GetRateLimitResetDelay(response);
        var fallback = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) + (Random.Shared.NextDouble() * 1.5));
        var delay = requestedDelay is { } value && value > TimeSpan.Zero ? value : fallback;
        return delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;
    }

    private static TimeSpan? GetRateLimitResetDelay(HttpResponseMessage response)
    {
        var delays = new List<TimeSpan>();
        foreach (var headerName in new[] { "x-ratelimit-reset-requests", "x-ratelimit-reset-tokens", "x-ratelimit-reset-project-tokens" })
        {
            if (!response.Headers.TryGetValues(headerName, out var values))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (TryParseRateLimitDuration(value, out var delay))
                {
                    delays.Add(delay);
                }
            }
        }

        return delays.Count == 0 ? null : delays.Max();
    }

    private static bool TryParseRateLimitDuration(string value, out TimeSpan duration)
    {
        var totalMilliseconds = 0d;
        foreach (Match match in Regex.Matches(value, @"(?<amount>\d+(?:\.\d+)?)(?<unit>ms|s|m|h)"))
        {
            var amount = double.Parse(match.Groups["amount"].Value, System.Globalization.CultureInfo.InvariantCulture);
            totalMilliseconds += match.Groups["unit"].Value switch
            {
                "ms" => amount,
                "s" => amount * 1_000,
                "m" => amount * 60_000,
                "h" => amount * 3_600_000,
                _ => 0
            };
        }

        duration = TimeSpan.FromMilliseconds(totalMilliseconds);
        return totalMilliseconds > 0;
    }


    private sealed record ApiResponse(HttpStatusCode StatusCode, string Json, int Attempts)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }

    private sealed record ApiError(string Message, string? Code, string? Param);
}
