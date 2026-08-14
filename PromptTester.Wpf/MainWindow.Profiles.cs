using System.IO;
using System.Windows;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

namespace PromptTester.Wpf;

public partial class MainWindow
{
    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder that contains PDFs or document images",
            Multiselect = false,
            InitialDirectory = Directory.Exists(ImageFolderTextBox.Text)
                ? ImageFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ImageFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseExportFile_Click(object sender, RoutedEventArgs e)
    {
        var currentPath = ExportFileTextBox.Text.Trim();
        var initialDirectory = Path.GetDirectoryName(currentPath);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Choose where to save the Excel report",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = Path.GetFileName(currentPath)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ExportFileTextBox.Text = dialog.FileName;
        }
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var model = GetSelectedModel();
            var profileName = ProfileComboBox.Text.Trim();
            if (_profileStore.Exists(profileName))
            {
                if (!AppDialog.Confirm(
                        this,
                        "Replace profile?",
                        $"Replace the existing profile '{profileName}'?",
                        "Replace profile"))
                {
                    return;
                }
            }

            var profile = new ProfileSettings
            {
                Name = profileName,
                Prompt = PromptTextBox.Text,
                Fields = FieldsTextBox.Text,
                Model = model.Id,
                InputDetail = GetSelectedInputDetail(),
                OpenAiLevel = _openAiLevelId,
                ImageFolderPath = ImageFolderTextBox.Text,
                ExportFilePath = ExportFileTextBox.Text
            };

            _profileStore.Save(profile);
            RefreshProfiles(profile.Name);
            StatusTextBlock.Text = $"Profile saved: {profile.Name}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = _profileStore.Load(ProfileComboBox.Text);
            ApplyProfile(profile);
            RefreshProfiles(profile.Name);
            StatusTextBlock.Text = ModelComboBox.SelectedItem is null
                ? $"Profile loaded: {profile.Name}. Choose a supported model before running."
                : $"Profile loaded: {profile.Name}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profileName = ProfileComboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                throw new InvalidOperationException("Choose a profile to delete.");
            }

            if (!_profileStore.Exists(profileName))
            {
                throw new FileNotFoundException($"Profile was not found: {profileName}");
            }

            if (!AppDialog.Confirm(
                    this,
                    "Delete profile?",
                    $"Delete profile '{profileName}'?",
                    "Delete profile"))
            {
                return;
            }

            _profileStore.Delete(profileName);
            RefreshProfiles();
            StatusTextBlock.Text = $"Profile deleted: {profileName}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }


    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(this) { Owner = this };
        _ = dialog.ShowDialog();
    }

    internal void ApplySettings(string apiKey, string? modelId, string? inputDetailId, string? openAiLevelId, bool isLightMode, string inputFolder, string reportPath)
    {
        ApiKeyPasswordBox.Password = apiKey.Trim();
        ModelComboBox.SelectedValue = modelId;
        InputDetailComboBox.SelectedValue = inputDetailId;
        _openAiLevelId = GetOpenAiLevel(openAiLevelId).Id;
        _isLightMode = isLightMode;
        ThemeManager.Apply(_isLightMode);
        AppPreferenceStore.SaveLightMode(_isLightMode);
        ApplyWindowChrome();
        ImageFolderTextBox.Text = inputFolder.Trim();
        ExportFileTextBox.Text = reportPath.Trim();
    }

    internal void SaveProfileFromSettings(string profileName)
    {
        ProfileComboBox.Text = profileName;
        SaveProfileButton_Click(this, new RoutedEventArgs());
    }

    internal void LoadProfileFromSettings(string profileName)
    {
        ProfileComboBox.Text = profileName;
        LoadProfileButton_Click(this, new RoutedEventArgs());
    }

    internal void DeleteProfileFromSettings(string profileName)
    {
        ProfileComboBox.Text = profileName;
        DeleteProfileButton_Click(this, new RoutedEventArgs());
    }


    private ModelDefinition GetSelectedModel()
    {
        return ModelComboBox.SelectedItem as ModelDefinition
            ?? throw new InvalidOperationException("Choose a supported model from the dropdown.");
    }

    private string GetSelectedInputDetail()
    {
        return InputDetailComboBox.SelectedItem is InputDetailOption detail
            ? detail.Id
            : throw new InvalidOperationException("Choose a visual detail level.");
    }

    private void RefreshProfiles(string? selectedProfile = null)
    {
        var profiles = _profileStore.GetProfileNames();
        ProfileComboBox.ItemsSource = profiles;

        if (!string.IsNullOrWhiteSpace(selectedProfile))
        {
            ProfileComboBox.Text = selectedProfile;
        }
        else if (profiles.Contains("Default", StringComparer.OrdinalIgnoreCase))
        {
            ProfileComboBox.Text = "Default";
        }
        else if (profiles.Count > 0)
        {
            ProfileComboBox.SelectedIndex = 0;
        }
        else
        {
            ProfileComboBox.Text = "";
        }
    }

    private void ApplyProfile(ProfileSettings profile)
    {
        PromptTextBox.Text = profile.Prompt;
        FieldsTextBox.Text = profile.Fields;
        SelectModel(profile.Model);

        var detail = InputDetailOptions.FirstOrDefault(option =>
            string.Equals(option.Id, profile.InputDetail, StringComparison.OrdinalIgnoreCase));
        InputDetailComboBox.SelectedItem = detail ?? InputDetailOptions[0];
        _openAiLevelId = GetOpenAiLevel(profile.OpenAiLevel).Id;
        ImageFolderTextBox.Text = profile.ImageFolderPath;
        ExportFileTextBox.Text = string.IsNullOrWhiteSpace(profile.ExportFilePath)
            ? BuildDefaultReportPath()
            : profile.ExportFilePath;
    }

    private void SelectModel(string? modelId)
    {
        var model = ModelCatalog.Find(modelId);
        ModelComboBox.SelectedItem = model;
        if (model is null && ModelSelectionWarningText is not null)
        {
            ModelSelectionWarningText.Text = $"The profile model '{modelId}' is not in the supported catalog. Choose a replacement.";
        }
    }

    private string GetExportFilePath()
    {
        var configuredPath = ExportFileTextBox.Text.Trim();
        var outputFolder = Path.HasExtension(configuredPath)
            ? Path.GetDirectoryName(configuredPath) ?? string.Empty
            : configuredPath;
        var reportPath = BuildReportPathInFolder(
            outputFolder,
            GetSelectedModel().Id,
            ImageFolderTextBox.Text);
        ExportFileTextBox.Text = reportPath;
        return reportPath;
    }

}
