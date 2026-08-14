namespace PromptTester.Wpf.Models;

public sealed class DocumentImageSet
{
    public string DocumentName { get; init; } = "";
    public string DocumentPath { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string? PdfPath { get; init; }
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
    public int InputCount => PdfPath is not null ? 1 : ImagePaths.Count;
}
