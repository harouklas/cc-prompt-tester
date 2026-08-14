namespace PromptTester.Wpf.Models;

public sealed class FieldDecision
{
    public string Status { get; set; } = "";
    public string? Evidence { get; set; }
    public string Explanation { get; set; } = "";
    public string Confidence { get; set; } = "";
}
