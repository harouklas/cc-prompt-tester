using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

/// <summary>
/// Models and Standard API token prices verified against OpenAI's official model and pricing pages on 2026-08-15.
/// Keep the dropdown and cost calculation on this single source of truth.
/// https://developers.openai.com/api/docs/models
/// https://developers.openai.com/api/docs/pricing
/// </summary>
public static class ModelCatalog
{
    private const int LongContextThreshold = 272_000;
    public const string PricingVerifiedDate = "2026-08-15";

    public static IReadOnlyList<ModelDefinition> Models { get; } =
    [
        new(
            "gpt-5.6-terra",
            "GPT-5.6 Terra",
            "Balanced intelligence and cost for production document extraction.",
            "Recommended",
            new(2.00m, 0.20m, 2.50m, 12.00m),
            SupportsReasoningSummary: true,
            new(4.00m, 0.40m, 5.00m, 18.00m),
            LongContextThreshold),
        new(
            "gpt-5.6-sol",
            "GPT-5.6 Sol",
            "Highest-quality GPT-5.6 option for difficult, ambiguous documents.",
            "Highest quality",
            new(5.00m, 0.50m, 6.25m, 30.00m),
            SupportsReasoningSummary: true,
            new(10.00m, 1.00m, 12.50m, 45.00m),
            LongContextThreshold),
        new(
            "gpt-5.6-luna",
            "GPT-5.6 Luna",
            "Efficient GPT-5.6 option for high-volume, cost-sensitive batches.",
            "Efficient",
            new(0.20m, 0.02m, 0.25m, 1.20m),
            SupportsReasoningSummary: true,
            new(0.40m, 0.04m, 0.50m, 1.80m),
            LongContextThreshold),
        new(
            "gpt-5.5",
            "GPT-5.5",
            "Prior-generation frontier model for complex professional work.",
            "High quality",
            new(5.00m, 0.50m, null, 30.00m),
            SupportsReasoningSummary: true,
            new(10.00m, 1.00m, null, 45.00m),
            LongContextThreshold),
        new(
            "gpt-5.4",
            "GPT-5.4",
            "Strong general-purpose reasoning and vision model.",
            "Strong value",
            new(2.50m, 0.25m, null, 15.00m),
            SupportsReasoningSummary: true,
            new(5.00m, 0.50m, null, 22.50m),
            LongContextThreshold),
        new(
            "gpt-5.4-mini",
            "GPT-5.4 mini",
            "Fast, capable model for most routine document batches.",
            "Fast batch",
            new(0.75m, 0.075m, null, 4.50m),
            SupportsReasoningSummary: true),
        new(
            "gpt-5.4-nano",
            "GPT-5.4 nano",
            "Lowest-cost GPT-5.4 option for simple, consistent layouts.",
            "Lowest cost",
            new(0.20m, 0.02m, null, 1.25m),
            SupportsReasoningSummary: true),
        new(
            "gpt-4.1",
            "GPT-4.1",
            "Reliable non-reasoning model with strong instruction following.",
            "Legacy",
            new(2.00m, 0.50m, null, 8.00m),
            SupportsReasoningSummary: false),
        new(
            "gpt-4.1-mini",
            "GPT-4.1 mini",
            "Fast non-reasoning model for straightforward extraction.",
            "Legacy fast",
            new(0.40m, 0.10m, null, 1.60m),
            SupportsReasoningSummary: false),
        new(
            "gpt-4o",
            "GPT-4o",
            "Established multimodal model retained for profile compatibility.",
            "Legacy vision",
            new(2.50m, 1.25m, null, 10.00m),
            SupportsReasoningSummary: false),
        new(
            "gpt-4o-mini",
            "GPT-4o mini",
            "Established low-cost multimodal model retained for compatibility.",
            "Legacy economy",
            new(0.15m, 0.075m, null, 0.60m),
            SupportsReasoningSummary: false)
    ];

    private static readonly IReadOnlyDictionary<string, ModelDefinition> ById = BuildIndex();

    public static ModelDefinition? Find(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) && ById.TryGetValue(modelId.Trim(), out var model)
            ? model
            : null;
    }

    public static CostEstimate CalculateCost(
        ModelDefinition model,
        int inputTokens,
        int cachedInputTokens,
        int cacheWriteTokens,
        int outputTokens)
    {
        inputTokens = Math.Max(0, inputTokens);
        outputTokens = Math.Max(0, outputTokens);
        cachedInputTokens = Math.Clamp(cachedInputTokens, 0, inputTokens);

        var useLongContextRates = model.LongContextRates is not null
            && model.LongContextThresholdTokens is int threshold
            && inputTokens > threshold;
        var rates = useLongContextRates ? model.LongContextRates! : model.StandardRates;

        var remainingAfterCached = inputTokens - cachedInputTokens;
        var separatelyPricedCacheWrites = rates.CacheWriteUsdPerMillion.HasValue
            ? Math.Clamp(cacheWriteTokens, 0, remainingAfterCached)
            : 0;
        var regularInputTokens = remainingAfterCached - separatelyPricedCacheWrites;

        var inputCost = CalculateTokenCost(regularInputTokens, rates.InputUsdPerMillion)
            + CalculateTokenCost(cachedInputTokens, rates.CachedInputUsdPerMillion)
            + CalculateTokenCost(separatelyPricedCacheWrites, rates.CacheWriteUsdPerMillion ?? 0m);
        var outputCost = CalculateTokenCost(outputTokens, rates.OutputUsdPerMillion);

        return new CostEstimate(
            inputCost,
            outputCost,
            inputCost + outputCost,
            useLongContextRates,
            useLongContextRates ? "Standard API - long context" : "Standard API");
    }

    private static decimal CalculateTokenCost(int tokens, decimal usdPerMillionTokens)
    {
        return tokens * usdPerMillionTokens / 1_000_000m;
    }

    private static IReadOnlyDictionary<string, ModelDefinition> BuildIndex()
    {
        var index = Models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);

        // The official gpt-5.6 alias routes to Sol. Keep it import-compatible without
        // duplicating the same choice in the visible dropdown.
        index["gpt-5.6"] = index["gpt-5.6-sol"];
        return index;
    }
}
