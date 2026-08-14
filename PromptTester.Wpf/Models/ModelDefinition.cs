using System.Globalization;

namespace PromptTester.Wpf.Models;

public sealed record TokenRates(
    decimal InputUsdPerMillion,
    decimal CachedInputUsdPerMillion,
    decimal? CacheWriteUsdPerMillion,
    decimal OutputUsdPerMillion);

public sealed record ModelDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Badge,
    TokenRates StandardRates,
    bool SupportsReasoningSummary,
    TokenRates? LongContextRates = null,
    int? LongContextThresholdTokens = null)
{
    public string PriceCaption
    {
        get
        {
            var caption = $"Per 1M tokens: {FormatRates(StandardRates)}";
            if (LongContextRates is not null && LongContextThresholdTokens is int threshold)
            {
                caption += $". Above {threshold / 1_000:N0}K input tokens: {FormatRates(LongContextRates)}";
            }

            return caption;
        }
    }

    private static string FormatRates(TokenRates rates)
    {
        var cacheWrite = rates.CacheWriteUsdPerMillion is decimal writeRate
            ? string.Create(CultureInfo.InvariantCulture, $" / ${writeRate:0.###} cache write")
            : "";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"${rates.InputUsdPerMillion:0.###} input / ${rates.CachedInputUsdPerMillion:0.###} cached{cacheWrite} / ${rates.OutputUsdPerMillion:0.###} output");
    }

    public override string ToString() => DisplayName;
}

public sealed record CostEstimate(
    decimal InputCostUsd,
    decimal OutputCostUsd,
    decimal TotalCostUsd,
    bool UsedLongContextPricing,
    string PricingLabel);
