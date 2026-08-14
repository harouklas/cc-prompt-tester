using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using PromptTester.Wpf.Animations;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

namespace PromptTester.Wpf;

public partial class MainWindow
{
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isScanning)
        {
            return;
        }

        var results = new List<ExtractionResult>();
        RunConfiguration? configuration = null;
        IReadOnlyList<DocumentImageSet> documents = [];
        string? logFolder = null;

        try
        {
            configuration = CaptureRunConfiguration();
            if (File.Exists(configuration.ReportPath))
            {
                if (!AppDialog.Confirm(
                        this,
                        "Replace Excel report?",
                        $"Replace the existing report?{Environment.NewLine}{configuration.ReportPath}",
                        "Replace report"))
                {
                    StatusTextBlock.Text = "Run cancelled before processing; the existing report was not changed.";
                    return;
                }
            }

            ExcelReportWriter.ValidateTarget(configuration.ReportPath);
            _cancellation = new CancellationTokenSource();
            SetRunningState(true);
            StartElapsedTimeTracking();
            StatusTextBlock.Text = "Validating and scanning the input folder...";
            documents = await Task.Run(
                () => ImageFileScanner.GetDocuments(configuration.InputFolderPath, _cancellation.Token),
                _cancellation.Token);
            if (documents.Count == 0)
            {
                ShowError("No supported PDFs or document images were found in the input folder.");
                return;
            }

            ExpandResultsPane();

            logFolder = DecisionLogWriter.CreateRunFolder(configuration.ReportPath);
            var tableState = CreateResultsTable(configuration.Fields);
            _activeDecisionLogColumnName = tableState.DecisionLog;
            ResultsGrid.ItemsSource = tableState.Table.DefaultView;
            ResultsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            ResultsEmptyStateText.Visibility = Visibility.Collapsed;

            var concurrency = Math.Min(configuration.OpenAiLevel.MaxConcurrency, documents.Count);
            var stopScheduling = false;
            StatusTextBlock.Text = $"Processing with up to {concurrency:N0} parallel request(s) for {configuration.OpenAiLevel.DisplayName}.";

            for (var batchStart = 0; batchStart < documents.Count && !stopScheduling; batchStart += concurrency)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                var batchEnd = Math.Min(batchStart + concurrency, documents.Count);
                var activeTasks = Enumerable.Range(batchStart, batchEnd - batchStart)
                    .Select(index => ProcessDocumentAsync(
                        index,
                        batchStart,
                        documents[index],
                        configuration,
                        logFolder,
                        _cancellation.Token))
                    .ToList();

                while (activeTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(activeTasks);
                    activeTasks.Remove(completedTask);
                    var completed = await completedTask;
                    results.Add(completed.Result);
                    AddResultRow(tableState, completed.Result, configuration.Fields);
                    UpdateSummary(results);
                    SetProgress((results.Count / (double)documents.Count) * 100);
                    StatusTextBlock.Text = $"Completed {results.Count:N0} of {documents.Count:N0}: {completed.Result.DocumentName}";

                    if (completed.Result.StopBatch)
                    {
                        stopScheduling = true;
                        StatusTextBlock.Text = $"Stopping after the active requests finish: {completed.Result.Error}";
                    }
                }
            }

            _cancellation.Token.ThrowIfCancellationRequested();
            _isFinalizing = true;
            CancelButton.IsEnabled = false;
            StatusTextBlock.Text = "Finalizing the Excel report atomically...";
            await Task.Run(() => ExcelReportWriter.Write(results, configuration.Fields, configuration.ReportPath));
            _isFinalizing = false;
            CompleteRunArtifacts(results, configuration.ReportPath, logFolder);

            var failures = results.Count(result => !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase));
            if (results.Count < documents.Count)
            {
                StatusTextBlock.Text = $"Stopped after {results.Count:N0} of {documents.Count:N0} documents. A partial Excel report and decision logs were saved.";
            }
            else if (failures > 0)
            {
                StatusTextBlock.Text = $"Completed with {failures:N0} failed document(s). Review the Excel report and decision logs.";
            }
            else
            {
                StatusTextBlock.Text = $"Completed successfully. Excel report and {results.Count:N0} decision log(s) are ready.";
            }

            if (results.Count == documents.Count)
            {
                CompletionAnnouncer.AnnounceRunCompleted();
                var completionDialog = new CompletionDialog(results.Count, failures)
                {
                    Owner = this
                };
                completionDialog.Show();
                completionDialog.Activate();
            }
        }
        catch (OperationCanceledException)
        {
            if (configuration is not null && results.Count > 0 && logFolder is not null)
            {
                try
                {
                    _isFinalizing = true;
                    CancelButton.IsEnabled = false;
                    StatusTextBlock.Text = "Finalizing the partial Excel report atomically...";
                    await Task.Run(() => ExcelReportWriter.Write(results, configuration.Fields, configuration.ReportPath));
                    _isFinalizing = false;
                    CompleteRunArtifacts(results, configuration.ReportPath, logFolder);
                    StatusTextBlock.Text = $"Run cancelled. A partial report with {results.Count:N0} completed document(s) was saved.";
                }
                catch (Exception exportException)
                {
                    StatusTextBlock.Text = $"Run cancelled after {results.Count:N0} document(s). Decision logs remain available, but the partial Excel report failed: {exportException.Message}";
                    _lastLogFolder = logFolder;
                    OpenLogsButton.IsEnabled = Directory.Exists(logFolder);
                }
            }
            else
            {
                StatusTextBlock.Text = "Run cancelled before any document completed.";
            }
        }
        catch (Exception ex)
        {
            if (results.Count > 0)
            {
                _lastResults = results.ToList();
                UpdateSummary(results);
            }

            if (!string.IsNullOrWhiteSpace(logFolder) && Directory.Exists(logFolder))
            {
                _lastLogFolder = logFolder;
                OpenLogsButton.IsEnabled = true;
            }

            ShowError(ex.Message);
        }
        finally
        {
            _isFinalizing = false;
            StopElapsedTimeTracking();
            SetRunningState(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task<(int Index, ExtractionResult Result)> ProcessDocumentAsync(
        int index,
        int batchStart,
        DocumentImageSet document,
        RunConfiguration configuration,
        string logFolder,
        CancellationToken cancellationToken)
    {
        var staggerPosition = index - batchStart;
        if (staggerPosition > 0 && configuration.OpenAiLevel.StaggerMilliseconds > 0)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(staggerPosition * configuration.OpenAiLevel.StaggerMilliseconds),
                cancellationToken);
        }

        var result = await _extractionService.ExtractAsync(
            document,
            configuration.Prompt,
            configuration.Fields,
            configuration.Model,
            configuration.InputDetail,
            configuration.ApiKey,
            cancellationToken);

        try
        {
            result.DecisionLogPath = DecisionLogWriter.Write(
                logFolder,
                index + 1,
                result,
                configuration.Fields,
                configuration.Prompt);
        }
        catch (Exception logException)
        {
            result.DecisionLogError = logException.Message;
            result.Warnings.Add($"The local decision log could not be written: {logException.Message}");
        }

        return (index, result);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFinalizing)
        {
            StatusTextBlock.Text = "The Excel report is being finalized atomically and cannot be interrupted safely.";
            return;
        }

        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "Cancelling safely; completed results will be exported...";
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isRunning)
        {
            return;
        }

        if (_isFinalizing)
        {
            e.Cancel = true;
            AppDialog.ShowMessage(
                this,
                "Finalizing report",
                "The Excel report is being finalized atomically. Please wait for completion before closing.");
            return;
        }

        var cancelRun = AppDialog.Confirm(
            this,
            "Batch in progress",
            "A batch is still running. Cancel it and keep this window open until partial results are saved?",
            "Cancel run",
            "Keep running");
        e.Cancel = true;
        if (cancelRun)
        {
            CancelButton_Click(this, new RoutedEventArgs());
        }
    }


    private RunConfiguration CaptureRunConfiguration()
    {
        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Add your OpenAI API key before running.");
        }

        var prompt = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Add an extraction prompt before running.");
        }

        var inputFolder = ImageFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
        {
            throw new DirectoryNotFoundException($"The input folder does not exist: {inputFolder}");
        }

        return new RunConfiguration(
            prompt,
            FieldParser.Parse(FieldsTextBox.Text),
            GetSelectedModel(),
            GetSelectedInputDetail(),
            GetOpenAiLevel(_openAiLevelId),
            apiKey,
            inputFolder,
            GetExportFilePath());
    }


    private void SetRunningState(bool isRunning)
    {
        _isRunning = isRunning;
        ConfigurationPanel.IsEnabled = !isRunning;
        RunButton.IsEnabled = !isRunning;
        PreviewButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning && !_isFinalizing;
        if (isRunning)
        {
            SetProgress(0);
            OpenReportButton.IsEnabled = false;
            OpenLogsButton.IsEnabled = false;
            OpenSelectedLogButton.IsEnabled = false;
        }
    }

    private void SetScanningState(bool isScanning)
    {
        _isScanning = isScanning;
        ConfigurationPanel.IsEnabled = !isScanning && !_isRunning;
        RunButton.IsEnabled = !isScanning && !_isRunning;
        PreviewButton.IsEnabled = !isScanning && !_isRunning;
    }

    private void SetProgress(double value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        ProgressBar.Value = clamped;
        ProgressPercentageText.Text = $"{clamped:0}%";
    }

    private void ExpandResultsPane()
    {
        var currentHeight = DefinitionRow.ActualHeight;
        DefinitionRow.MinHeight = 0;
        if (currentHeight <= 0)
        {
            DefinitionRow.Height = new GridLength(0);
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            DefinitionRow.Height = new GridLength(0);
            return;
        }

        var animation = new GridLengthAnimation
        {
            From = new GridLength(currentHeight, GridUnitType.Pixel),
            To = new GridLength(0, GridUnitType.Pixel),
            Duration = TimeSpan.FromMilliseconds(440),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => DefinitionRow.Height = new GridLength(0);
        DefinitionRow.BeginAnimation(RowDefinition.HeightProperty, animation);
    }

    private void StartElapsedTimeTracking()
    {
        _elapsedTimeTimer.Stop();
        _runStopwatch.Restart();
        UpdateElapsedTimeMetric();
        _elapsedTimeTimer.Start();
    }

    private void StopElapsedTimeTracking()
    {
        _elapsedTimeTimer.Stop();
        _runStopwatch.Stop();
        UpdateElapsedTimeMetric();
    }

    private void UpdateElapsedTimeMetric()
    {
        var elapsed = _runStopwatch.Elapsed;
        ElapsedTimeMetricText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateSummary(IReadOnlyList<ExtractionResult> results)
    {
        var succeeded = results.Count(result => string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase));
        var failed = results.Count - succeeded;
        var tokens = results.Sum(result => (long)result.TotalTokens);
        var totalCost = results.Where(result => result.HasCostEstimate).Sum(result => result.TotalCostUsd);

        DocumentsMetricText.Text = results.Count.ToString("N0", CultureInfo.InvariantCulture);
        SucceededMetricText.Text = succeeded.ToString("N0", CultureInfo.InvariantCulture);
        FailedMetricText.Text = failed.ToString("N0", CultureInfo.InvariantCulture);
        TokensMetricText.Text = tokens.ToString("N0", CultureInfo.InvariantCulture);
        CostMetricText.Text = $"${totalCost.ToString("0.00000000", CultureInfo.InvariantCulture)}";
        ResultCountText.Text = results.Count == 0 ? "No results yet" : $"{results.Count:N0} processed document(s)";
    }

    private void UpdateEditorCounters()
    {
        if (PromptCharacterCountText is not null && PromptTextBox is not null)
        {
            PromptCharacterCountText.Text = $"{PromptTextBox.Text.Length:N0} characters";
        }

        if (FieldCountText is not null && FieldsTextBox is not null)
        {
            var count = FieldsTextBox.Text
                .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            FieldCountText.Text = $"{count:N0} field(s)";
        }
    }

}
