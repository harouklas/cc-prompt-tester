using System.Windows;

namespace PromptTester.Wpf;

public partial class AppDialog : Window
{
    private AppDialog(string title, string message, string primaryButtonText, string? secondaryButtonText)
    {
        InitializeComponent();
        DialogTitleTextBlock.Text = title;
        DialogMessageTextBlock.Text = message;
        PrimaryButton.Content = primaryButtonText;

        if (string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryButtonText;
        }
    }

    public static bool Confirm(Window owner, string title, string message, string primaryButtonText, string secondaryButtonText = "Cancel")
    {
        var dialog = new AppDialog(title, message, primaryButtonText, secondaryButtonText)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    public static void ShowMessage(Window owner, string title, string message, string buttonText = "Okay")
    {
        var dialog = new AppDialog(title, message, buttonText, null)
        {
            Owner = owner
        };
        _ = dialog.ShowDialog();
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
