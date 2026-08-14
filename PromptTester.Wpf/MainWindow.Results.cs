using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

namespace PromptTester.Wpf;

public partial class MainWindow
{
    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isScanning)
        {
            return;
        }

        try
        {
            SetScanningState(true);
            StatusTextBlock.Text = "Scanning the input folder...";
            var folderPath = ImageFolderTextBox.Text.Trim();
            var documents = await Task.Run(() => ImageFileScanner.GetDocuments(folderPath));
            ResultsGrid.ItemsSource = BuildPreviewTable(documents).DefaultView;
            ResultsGrid.HeadersVisibility = documents.Count > 0
                ? DataGridHeadersVisibility.Column
                : DataGridHeadersVisibility.None;
            ResultsEmptyStateText.Text = documents.Count == 0
                ? "No supported PDFs or images were found."
                : "";
            ResultsEmptyStateText.Visibility = documents.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _activeDecisionLogColumnName = null;
            ResultCountText.Text = $"{documents.Count:N0} document(s) found";
            DocumentsMetricText.Text = documents.Count.ToString("N0", CultureInfo.InvariantCulture);
            SucceededMetricText.Text = "0";
            FailedMetricText.Text = "0";
            TokensMetricText.Text = "0";
            CostMetricText.Text = "$0.00000000";
            OpenSelectedLogButton.IsEnabled = false;
            StatusTextBlock.Text = documents.Count == 0
                ? "No supported PDFs or images were found."
                : $"Scan complete: {documents.Count:N0} document(s). Each PDF is one document; every image folder is processed as one document.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetScanningState(false);
        }
    }


    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocalPath(_lastReportPath, "Excel report");
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocalPath(_lastLogFolder, "decision-log folder");
    }

    private void OpenSelectedLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not DataRowView row
            || string.IsNullOrWhiteSpace(_activeDecisionLogColumnName)
            || !row.Row.Table.Columns.Contains(_activeDecisionLogColumnName))
        {
            ShowError("Select a processed document that has a decision log.");
            return;
        }

        OpenLocalPath(row[_activeDecisionLogColumnName]?.ToString(), "selected decision log");
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenSelectedLogButton.IsEnabled = ResultsGrid.SelectedItem is DataRowView row
            && !string.IsNullOrWhiteSpace(_activeDecisionLogColumnName)
            && row.Row.Table.Columns.Contains(_activeDecisionLogColumnName)
            && File.Exists(row[_activeDecisionLogColumnName]?.ToString());
    }

    private void ResultsGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var header = e.PropertyName;
        e.Column.Header = header;
        e.Column.MinWidth = 90;

        if (header is "Document Name")
        {
            e.Column.Width = new DataGridLength(190);
        }
        else if (header is "Document Path" or "Error" or "Decision Summary" or "Decision Log Error"
                 || string.Equals(header, _activeDecisionLogColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Column.Width = new DataGridLength(300);
        }
        else if (header.Contains("Tokens", StringComparison.OrdinalIgnoreCase)
                 || header.Contains("Cost", StringComparison.OrdinalIgnoreCase)
                 || header is "Input Count" or "Processing Seconds")
        {
            e.Column.Width = new DataGridLength(125);
        }
        else
        {
            e.Column.Width = new DataGridLength(155);
        }

        if (e.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
        {
            if (header.Contains("Cost", StringComparison.OrdinalIgnoreCase))
            {
                binding.StringFormat = "0.00000000";
            }
            else if (header is "Processing Seconds")
            {
                binding.StringFormat = "0.00";
            }
        }
    }

    private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditorCounters();
    }

    private void FieldsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditorCounters();
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentModelBadgeText is not null)
        {
            CurrentModelBadgeText.Text = ModelComboBox.SelectedItem is ModelDefinition selectedModel
                ? $"Current model: {selectedModel.DisplayName}"
                : "Current model: Not selected";
        }

        if (ModelDescriptionText is null || ModelPriceText is null || ModelSelectionWarningText is null)
        {
            return;
        }

        if (ModelComboBox.SelectedItem is ModelDefinition model)
        {
            ModelDescriptionText.Text = model.Description;
            ModelPriceText.Text = $"{model.PriceCaption}. Prices verified {ModelCatalog.PricingVerifiedDate}; requests pin the Standard/default service tier.";
            ModelSelectionWarningText.Text = "";
        }
        else
        {
            ModelDescriptionText.Text = "Select a supported document extraction model.";
            ModelPriceText.Text = "Every listed model has a corresponding cost rule.";
        }
    }


    private static DataTable BuildPreviewTable(IReadOnlyList<DocumentImageSet> documents)
    {
        var table = new DataTable();
        table.Columns.Add("Document Name", typeof(string));
        table.Columns.Add("Document Path", typeof(string));
        table.Columns.Add("Source Type", typeof(string));
        table.Columns.Add("Input Count", typeof(int));

        foreach (var document in documents)
        {
            table.Rows.Add(document.DocumentName, document.DocumentPath, document.SourceType, document.InputCount);
        }

        return table;
    }

    private static ResultsTableState CreateResultsTable(IReadOnlyList<string> fields)
    {
        var table = new DataTable();
        var state = new ResultsTableState(table)
        {
            DocumentName = AddUniqueColumn(table, "Document Name", typeof(string)),
            DocumentPath = AddUniqueColumn(table, "Document Path", typeof(string)),
            SourceType = AddUniqueColumn(table, "Source Type", typeof(string)),
            InputCount = AddUniqueColumn(table, "Input Count", typeof(int)),
            InputTokens = AddUniqueColumn(table, "Input Tokens", typeof(int)),
            CachedInputTokens = AddUniqueColumn(table, "Cached Input Tokens", typeof(int)),
            CacheWriteTokens = AddUniqueColumn(table, "Cache Write Tokens", typeof(int)),
            OutputTokens = AddUniqueColumn(table, "Output Tokens", typeof(int)),
            ReasoningTokens = AddUniqueColumn(table, "Reasoning Tokens", typeof(int)),
            TotalTokens = AddUniqueColumn(table, "Total Tokens", typeof(int)),
            InputCost = AddUniqueColumn(table, "Input Cost USD", typeof(decimal)),
            OutputCost = AddUniqueColumn(table, "Output Cost USD", typeof(decimal)),
            TotalCost = AddUniqueColumn(table, "Total Cost USD", typeof(decimal))
        };

        foreach (var field in fields)
        {
            state.FieldColumns[field] = AddUniqueColumn(table, ToHeaderLabel(field), typeof(string));
        }

        state.Status = AddUniqueColumn(table, "Status", typeof(string));
        state.Error = AddUniqueColumn(table, "Error", typeof(string));
        state.Model = AddUniqueColumn(table, "Model", typeof(string));
        state.ResponseId = AddUniqueColumn(table, "Response ID", typeof(string));
        state.ApiStatus = AddUniqueColumn(table, "API Status", typeof(string));
        state.PricingTier = AddUniqueColumn(table, "Pricing Tier", typeof(string));
        state.DecisionSummary = AddUniqueColumn(table, "Decision Summary", typeof(string));
        state.DecisionLog = AddUniqueColumn(table, DecisionLogColumnName, typeof(string));
        state.DecisionLogError = AddUniqueColumn(table, "Decision Log Error", typeof(string));
        state.ProcessingSeconds = AddUniqueColumn(table, "Processing Seconds", typeof(double));
        return state;
    }

    private static void AddResultRow(
        ResultsTableState state,
        ExtractionResult result,
        IReadOnlyList<string> fields)
    {
        var row = state.Table.NewRow();
        row[state.DocumentName] = result.DocumentName;
        row[state.DocumentPath] = result.DocumentPath;
        row[state.SourceType] = result.SourceType;
        row[state.InputCount] = result.ImageCount;
        row[state.InputTokens] = result.InputTokens;
        row[state.CachedInputTokens] = result.CachedInputTokens;
        row[state.CacheWriteTokens] = result.CacheWriteTokens;
        row[state.OutputTokens] = result.OutputTokens;
        row[state.ReasoningTokens] = result.ReasoningTokens;
        row[state.TotalTokens] = result.TotalTokens;
        row[state.InputCost] = result.HasCostEstimate ? result.InputCostUsd : DBNull.Value;
        row[state.OutputCost] = result.HasCostEstimate ? result.OutputCostUsd : DBNull.Value;
        row[state.TotalCost] = result.HasCostEstimate ? result.TotalCostUsd : DBNull.Value;

        foreach (var field in fields)
        {
            row[state.FieldColumns[field]] = result.Values.TryGetValue(field, out var value)
                ? value ?? (object)DBNull.Value
                : DBNull.Value;
        }

        row[state.Status] = result.Status;
        row[state.Error] = result.Error;
        row[state.Model] = result.Model;
        row[state.ResponseId] = result.ResponseId;
        row[state.ApiStatus] = result.CompletionStatus;
        row[state.PricingTier] = result.PricingLabel;
        row[state.DecisionSummary] = result.DecisionSummary;
        row[state.DecisionLog] = result.DecisionLogPath;
        row[state.DecisionLogError] = result.DecisionLogError;
        row[state.ProcessingSeconds] = result.ProcessingSeconds;
        state.Table.Rows.Add(row);
    }

    private static string AddUniqueColumn(DataTable table, string preferredName, Type type)
    {
        var columnName = preferredName;
        var suffix = 2;
        while (table.Columns.Contains(columnName))
        {
            columnName = $"{preferredName} {suffix++}";
        }

        var column = table.Columns.Add(columnName, type);
        return column.ColumnName;
    }

    private static string ToHeaderLabel(string value)
    {
        var label = string.Join(
            " ",
            value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())));
        return string.IsNullOrWhiteSpace(label) ? value : label;
    }


    private void CompleteRunArtifacts(
        IReadOnlyList<ExtractionResult> results,
        string reportPath,
        string logFolder)
    {
        _lastResults = results.ToList();
        _lastReportPath = reportPath;
        _lastLogFolder = logFolder;
        OpenReportButton.IsEnabled = File.Exists(reportPath);
        OpenLogsButton.IsEnabled = Directory.Exists(logFolder);
        UpdateSummary(results);
    }

    private void OpenLocalPath(string? path, string description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                throw new FileNotFoundException($"The {description} is not available yet.", path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        StatusTextBlock.Text = message;
        AppDialog.ShowMessage(this, "Something needs attention", message, "Understood");
    }

}
