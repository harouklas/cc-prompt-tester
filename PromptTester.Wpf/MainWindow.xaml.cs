using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

namespace PromptTester.Wpf;

public partial class MainWindow : Window
{
    private const string DecisionLogColumnName = "Decision Log";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

    internal static readonly IReadOnlyList<InputDetailOption> InputDetailOptions =
    [
        new("low", "Low - cost controlled", "Fast, low-cost reading for clear documents"),
        new("high", "High - fine detail", "Higher fidelity for small text and dense layouts"),
        new("auto", "Auto - model default", "GPT-5.5/5.6 may use original-size images")
    ];

    internal static readonly IReadOnlyList<OpenAiLevelOption> OpenAiLevels =
    [
        new("free", "Free", "Single request at a time", 1, 0),
        new("tier1", "Tier 1", "Single request at a time", 1, 0),
        new("tier2", "Tier 2", "Up to 2 documents in parallel", 2, 350),
        new("tier3", "Tier 3", "Up to 3 documents in parallel", 3, 300),
        new("tier4", "Tier 4", "Up to 5 documents in parallel", 5, 250),
        new("tier5", "Tier 5", "Up to 8 documents in parallel", 8, 200)
    ];

    private readonly OpenAiExtractionService _extractionService = new();
    private readonly ProfileStore _profileStore = new(GetProjectRoot());
    private readonly Stopwatch _runStopwatch = new();
    private readonly DispatcherTimer _elapsedTimeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _cancellation;
    private string _openAiLevelId = "tier5";
    private bool _isLightMode = AppPreferenceStore.LoadLightMode();

    internal string ApiKey => ApiKeyPasswordBox.Password;
    internal string? ModelId => ModelComboBox.SelectedValue as string;
    internal string? InputDetailId => InputDetailComboBox.SelectedValue as string;
    internal string InputFolder => ImageFolderTextBox.Text;
    internal string ReportPath => ExportFileTextBox.Text;
    internal string ProfileName => ProfileComboBox.Text;
    internal string OpenAiLevelId => _openAiLevelId;
    internal bool IsLightMode => _isLightMode;
    internal IReadOnlyList<string> ProfileNames => _profileStore.GetProfileNames();
    private IReadOnlyList<ExtractionResult> _lastResults = [];
    private string? _lastReportPath;
    private string? _lastLogFolder;
    private string? _activeDecisionLogColumnName;
    private bool _isRunning;
    private bool _isScanning;
    private bool _isFinalizing;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Apply(_isLightMode);
        _elapsedTimeTimer.Tick += (_, _) => UpdateElapsedTimeMetric();
        FitWindowToCurrentWorkArea();
        InitializeCatalogs();
        LoadDefaults();
        RefreshProfiles();
        LoadStartupProfile();
        UpdateEditorCounters();
        UpdateSummary([]);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        if (SystemParameters.HighContrast)
        {
            return;
        }

        var windowHandle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        if (DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }

        var captionColor = _isLightMode ? ToColorRef(245, 248, 251) : ToColorRef(7, 16, 24);
        var textColor = _isLightMode ? ToColorRef(22, 32, 42) : ToColorRef(244, 248, 251);
        var borderColor = ToColorRef(22, 184, 243);
        _ = DwmSetWindowAttribute(windowHandle, DwmCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmTextColor, ref textColor, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmBorderColor, ref borderColor, sizeof(int));
    }

    private static int ToColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }

    private void FitWindowToCurrentWorkArea()
    {
        const double workAreaPadding = 24;
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(640, workArea.Width - workAreaPadding);
        var availableHeight = Math.Max(520, workArea.Height - workAreaPadding);

        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PromptTextBox.CaretIndex = 0;
            PromptTextBox.ScrollToHome();
            FieldsTextBox.CaretIndex = 0;
            FieldsTextBox.ScrollToHome();
            ConfigurationScrollViewer.ScrollToTop();
            Keyboard.ClearFocus();
        }, DispatcherPriority.ContextIdle);
    }

    private void InitializeCatalogs()
    {
        ModelComboBox.ItemsSource = ModelCatalog.Models;
        InputDetailComboBox.ItemsSource = InputDetailOptions;
    }

    private void LoadDefaults()
    {
        PromptTextBox.Text = """
            Extract the requested fields from the provided document.

            Rules:
            - Return values exactly as seen in the document when possible.
            - Preserve leading zeros and visible date/number formatting.
            - If a field is missing, return null for that field.
            - Do not invent values.
            - If a field has multiple values, return all values in document order.
            """;

        FieldsTextBox.Text = """
            document_type
            document_number
            document_date
            supplier_name
            customer_name
            total_amount
            currency
            """;

        SelectModel("gpt-5.6-terra");
        InputDetailComboBox.SelectedValue = "low";
        ImageFolderTextBox.Text = "";
        ExportFileTextBox.Text = BuildDefaultReportPath();
    }

    private void LoadStartupProfile()
    {
        try
        {
            if (!_profileStore.Exists("Default"))
            {
                return;
            }

            var profile = _profileStore.Load("Default");
            ApplyProfile(profile);
            ProfileComboBox.Text = profile.Name;
            StatusTextBlock.Text = "Default profile loaded. Add an API key and choose an input folder.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Defaults loaded; the saved Default profile could not be loaded: {ex.Message}";
        }
    }

    private static string GetProjectRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 6; index++)
        {
            if (Directory.Exists(Path.Combine(directory, "outputs"))
                || File.Exists(Path.Combine(directory, "PromptTester.sln")))
            {
                return directory;
            }

            var parent = Directory.GetParent(directory);
            if (parent is null)
            {
                break;
            }

            directory = parent.FullName;
        }

        return AppContext.BaseDirectory;
    }

    private static string BuildDefaultReportPath()
    {
        var projectOutputs = Path.Combine(GetProjectRoot(), "outputs");
        var outputFolder = Directory.Exists(projectOutputs)
            ? projectOutputs
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prompt Tester", "Reports");
        return Path.Combine(outputFolder, BuildDefaultReportFileName());
    }

    private static string BuildDefaultReportFileName()
    {
        return $"extraction_report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }

    internal static string BuildReportPathInFolder(string outputFolder, string? modelId, string? inputFolder)
    {
        var folder = outputFolder.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.GetDirectoryName(BuildDefaultReportPath())
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var modelName = SanitizeFileNamePart(modelId, "model");
        var normalizedInputFolder = string.IsNullOrWhiteSpace(inputFolder)
            ? string.Empty
            : Path.TrimEndingDirectorySeparator(inputFolder.Trim());
        var inputFolderName = SanitizeFileNamePart(Path.GetFileName(normalizedInputFolder), "input");
        var reportFileName = $"{DateTime.Now:ddMMyy-HH.mm}-{modelName}-{inputFolderName}.xlsx";
        return Path.Combine(folder, reportFileName);
    }

    private static string SanitizeFileNamePart(string? value, string fallback)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalidCharacter, '-');
        }

        cleaned = string.Join("-", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    internal sealed record InputDetailOption(string Id, string DisplayName, string Description);

    internal sealed record OpenAiLevelOption(
        string Id,
        string DisplayName,
        string Description,
        int MaxConcurrency,
        int StaggerMilliseconds);

    private static OpenAiLevelOption GetOpenAiLevel(string? levelId)
    {
        return OpenAiLevels.FirstOrDefault(level => string.Equals(level.Id, levelId, StringComparison.OrdinalIgnoreCase))
            ?? OpenAiLevels.First(level => level.Id == "tier1");
    }

    private sealed record RunConfiguration(
        string Prompt,
        IReadOnlyList<string> Fields,
        ModelDefinition Model,
        string InputDetail,
        OpenAiLevelOption OpenAiLevel,
        string ApiKey,
        string InputFolderPath,
        string ReportPath);

    private sealed class ResultsTableState(DataTable table)
    {
        public DataTable Table { get; } = table;
        public Dictionary<string, string> FieldColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string DocumentName { get; set; } = "";
        public string DocumentPath { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string InputCount { get; set; } = "";
        public string InputTokens { get; set; } = "";
        public string CachedInputTokens { get; set; } = "";
        public string CacheWriteTokens { get; set; } = "";
        public string OutputTokens { get; set; } = "";
        public string ReasoningTokens { get; set; } = "";
        public string TotalTokens { get; set; } = "";
        public string InputCost { get; set; } = "";
        public string OutputCost { get; set; } = "";
        public string TotalCost { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
        public string Model { get; set; } = "";
        public string ResponseId { get; set; } = "";
        public string ApiStatus { get; set; } = "";
        public string PricingTier { get; set; } = "";
        public string DecisionSummary { get; set; } = "";
        public string DecisionLog { get; set; } = "";
        public string DecisionLogError { get; set; } = "";
        public string ProcessingSeconds { get; set; } = "";
    }
}
