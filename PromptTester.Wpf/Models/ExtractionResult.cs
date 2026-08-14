namespace PromptTester.Wpf.Models;

public sealed class ExtractionResult
{
    public string DocumentName { get; set; } = "";
    public string DocumentPath { get; set; } = "";
    public string SourceType { get; set; } = "";
    public int ImageCount { get; set; }
    public string Status { get; set; } = "";
    public string Error { get; set; } = "";
    public string Model { get; set; } = "";
    public string ResponseId { get; set; } = "";
    public string CompletionStatus { get; set; } = "";
    public bool StopBatch { get; set; }
    public int InputTokens { get; set; }
    public int CachedInputTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal InputCostUsd { get; set; }
    public decimal OutputCostUsd { get; set; }
    public decimal TotalCostUsd { get; set; }
    public bool HasCostEstimate { get; set; }
    public bool UsedLongContextPricing { get; set; }
    public string PricingLabel { get; set; } = "";
    public double ProcessingSeconds { get; set; }
    public string DecisionSummary { get; set; } = "";
    public string ModelReasoningSummary { get; set; } = "";
    public string DecisionLogPath { get; set; } = "";
    public string DecisionLogError { get; set; } = "";
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FieldDecision> FieldDecisions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; } = [];
}
