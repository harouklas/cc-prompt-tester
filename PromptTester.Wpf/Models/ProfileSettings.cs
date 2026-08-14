namespace PromptTester.Wpf.Models;

public sealed class ProfileSettings
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Fields { get; set; } = "";
    public string Model { get; set; } = "";
    public string InputDetail { get; set; } = "low";
    public string OpenAiLevel { get; set; } = "tier1";
    public string ImageFolderPath { get; set; } = "";
    public string ExportFilePath { get; set; } = "";
}
