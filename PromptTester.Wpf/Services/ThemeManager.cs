using System.Windows;
using System.Windows.Media;

namespace PromptTester.Wpf.Services;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["BrandBlackBrush"] = "#071018",
        ["CanvasBrush"] = "#0A1118",
        ["SurfaceBrush"] = "#101A24",
        ["SurfaceRaisedBrush"] = "#172432",
        ["SurfaceHoverBrush"] = "#203244",
        ["PrimaryBrush"] = "#16B8F3",
        ["PrimaryHoverBrush"] = "#42C7F7",
        ["PrimaryPressedBrush"] = "#0B8FC6",
        ["AccentBrush"] = "#16B8F3",
        ["AccentHoverBrush"] = "#42C7F7",
        ["AccentTextBrush"] = "#6FD8FF",
        ["AccentSoftBrush"] = "#0B2430",
        ["AccentSelectionBrush"] = "#125D78",
        ["OnAccentBrush"] = "#071018",
        ["TextBrush"] = "#F4F8FB",
        ["MutedTextBrush"] = "#B4C1CC",
        ["SubtleTextBrush"] = "#8798A7",
        ["BorderBrush"] = "#304354",
        ["BorderStrongBrush"] = "#6B8192",
        ["FocusBrush"] = "#6FD8FF",
        ["ProgressTrackBrush"] = "#263746",
        ["SuccessBrush"] = "#9BE15D",
        ["SuccessSoftBrush"] = "#172510",
        ["WarningBrush"] = "#FFD166",
        ["WarningSoftBrush"] = "#2D2510",
        ["WarningBorderBrush"] = "#806A25",
        ["ErrorBrush"] = "#FF7B88",
        ["ErrorSoftBrush"] = "#30151B",
        ["ErrorBorderBrush"] = "#8F3944"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["BrandBlackBrush"] = "#FFFFFF",
        ["CanvasBrush"] = "#F5F8FB",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceRaisedBrush"] = "#EDF3F8",
        ["SurfaceHoverBrush"] = "#E1EBF3",
        ["PrimaryBrush"] = "#0078D4",
        ["PrimaryHoverBrush"] = "#005A9E",
        ["PrimaryPressedBrush"] = "#004578",
        ["AccentBrush"] = "#0078D4",
        ["AccentHoverBrush"] = "#005A9E",
        ["AccentTextBrush"] = "#0067B1",
        ["AccentSoftBrush"] = "#E1F4FF",
        ["AccentSelectionBrush"] = "#9AD9F7",
        ["OnAccentBrush"] = "#FFFFFF",
        ["TextBrush"] = "#16202A",
        ["MutedTextBrush"] = "#526474",
        ["SubtleTextBrush"] = "#758899",
        ["BorderBrush"] = "#CFDCE6",
        ["BorderStrongBrush"] = "#8CA1B2",
        ["FocusBrush"] = "#0067B1",
        ["ProgressTrackBrush"] = "#DFE8EF",
        ["SuccessBrush"] = "#317A1A",
        ["SuccessSoftBrush"] = "#E7F4E2",
        ["WarningBrush"] = "#8A5A00",
        ["WarningSoftBrush"] = "#FFF2CF",
        ["WarningBorderBrush"] = "#C99A33",
        ["ErrorBrush"] = "#B42335",
        ["ErrorSoftBrush"] = "#FCE5E8",
        ["ErrorBorderBrush"] = "#D76A77"
    };

    public static void Apply(bool useLightMode)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        foreach (var entry in useLightMode ? LightPalette : DarkPalette)
        {
            ReplaceResource(resources, entry.Key, new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.Value)));
        }
    }

    private static bool ReplaceResource(ResourceDictionary dictionary, string key, SolidColorBrush brush)
    {
        if (dictionary.Contains(key))
        {
            dictionary[key] = brush;
            return true;
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (ReplaceResource(mergedDictionary, key, brush))
            {
                return true;
            }
        }

        return false;
    }
}
