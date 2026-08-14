using System.IO;
using System.Windows;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

namespace PromptTester.Wpf;

public partial class SettingsDialog : Window
{
    private readonly MainWindow _mainWindow;

    public SettingsDialog(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        ModelComboBox.ItemsSource = ModelCatalog.Models;
        InputDetailComboBox.ItemsSource = MainWindow.InputDetailOptions;
        OpenAiLevelComboBox.ItemsSource = MainWindow.OpenAiLevels;
        ApiKeyPasswordBox.Password = mainWindow.ApiKey;
        ModelComboBox.SelectedValue = mainWindow.ModelId;
        InputDetailComboBox.SelectedValue = mainWindow.InputDetailId;
        OpenAiLevelComboBox.SelectedValue = mainWindow.OpenAiLevelId;
        ThemeModeToggle.IsChecked = mainWindow.IsLightMode;
        InputFolderPathText.Text = mainWindow.InputFolder;
        OutputFolderPathText.Text = GetReportFolder(mainWindow.ReportPath);
        RefreshProfiles(mainWindow.ProfileName);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVisibleSettings();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVisibleSettings();
        var profileName = ProfileComboBox.Text.Trim();
        _mainWindow.SaveProfileFromSettings(profileName);
        RefreshProfiles(profileName);
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profileName = ProfileComboBox.Text.Trim();
        _mainWindow.LoadProfileFromSettings(profileName);
        RefreshVisibleSettings();
        RefreshProfiles(profileName);
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profileName = ProfileComboBox.Text.Trim();
        _mainWindow.DeleteProfileFromSettings(profileName);
        RefreshProfiles(null);
    }

    private void ApplyVisibleSettings()
    {
        _mainWindow.ApplySettings(
            ApiKeyPasswordBox.Password,
            ModelComboBox.SelectedValue as string,
            InputDetailComboBox.SelectedValue as string,
            OpenAiLevelComboBox.SelectedValue as string,
            ThemeModeToggle.IsChecked == true,
            InputFolderPathText.Text,
            MainWindow.BuildReportPathInFolder(
                OutputFolderPathText.Text,
                ModelComboBox.SelectedValue as string,
                InputFolderPathText.Text));
    }

    private void RefreshVisibleSettings()
    {
        ApiKeyPasswordBox.Password = _mainWindow.ApiKey;
        ModelComboBox.SelectedValue = _mainWindow.ModelId;
        InputDetailComboBox.SelectedValue = _mainWindow.InputDetailId;
        OpenAiLevelComboBox.SelectedValue = _mainWindow.OpenAiLevelId;
        ThemeModeToggle.IsChecked = _mainWindow.IsLightMode;
        InputFolderPathText.Text = _mainWindow.InputFolder;
        OutputFolderPathText.Text = GetReportFolder(_mainWindow.ReportPath);
    }

    private void RefreshProfiles(string? selectedProfile)
    {
        ProfileComboBox.ItemsSource = _mainWindow.ProfileNames;
        ProfileComboBox.Text = string.IsNullOrWhiteSpace(selectedProfile) ? string.Empty : selectedProfile;
    }

    private void InputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        InputFolderPathText.Text = ChooseFolder(
            "Choose the folder containing PDFs or document images",
            InputFolderPathText.Text);
    }

    private void OutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OutputFolderPathText.Text = ChooseFolder(
            "Choose where the Excel report and decision logs will be created",
            OutputFolderPathText.Text);
    }

    private string ChooseFolder(string title, string currentFolder)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = Directory.Exists(currentFolder)
                ? currentFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog(this) == true ? dialog.FolderName : currentFolder;
    }

    private static string GetReportFolder(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return string.Empty;
        }

        return Path.HasExtension(reportPath)
            ? Path.GetDirectoryName(reportPath) ?? string.Empty
            : reportPath;
    }
}
