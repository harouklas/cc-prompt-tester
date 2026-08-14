using System.IO;

namespace PromptTester.Wpf.Services;

internal static class AppPreferenceStore
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CChaliotis",
        "PromptTester",
        "theme.txt");

    public static bool LoadLightMode()
    {
        try
        {
            return File.Exists(PreferencePath)
                && string.Equals(File.ReadAllText(PreferencePath).Trim(), "light", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SaveLightMode(bool useLightMode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, useLightMode ? "light" : "dark");
        }
        catch
        {
            // A preference write failure must not block settings or extraction.
        }
    }
}
