using System.Windows;

namespace PromptTester.Wpf;

public partial class CompletionDialog : Window
{
    public CompletionDialog(int processedDocuments, int failedDocuments)
    {
        InitializeComponent();
        SummaryTextBlock.Text = failedDocuments == 0
            ? $"{processedDocuments:N0} document{(processedDocuments == 1 ? string.Empty : "s")} processed successfully."
            : $"{processedDocuments:N0} document{(processedDocuments == 1 ? string.Empty : "s")} processed; {failedDocuments:N0} need review in the report.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
