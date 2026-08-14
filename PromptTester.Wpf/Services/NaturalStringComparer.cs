using System.Text.RegularExpressions;

namespace PromptTester.Wpf.Services;

internal sealed class NaturalStringComparer : IComparer<string>
{
    private static readonly Regex NumericParts = new("(\\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xParts = NumericParts.Split(x);
        var yParts = NumericParts.Split(y);
        for (var index = 0; index < Math.Min(xParts.Length, yParts.Length); index++)
        {
            var xPart = xParts[index];
            var yPart = yParts[index];
            int comparison;

            if (index % 2 == 1)
            {
                var xSignificant = xPart.TrimStart('0');
                var ySignificant = yPart.TrimStart('0');
                xSignificant = xSignificant.Length == 0 ? "0" : xSignificant;
                ySignificant = ySignificant.Length == 0 ? "0" : ySignificant;

                comparison = xSignificant.Length.CompareTo(ySignificant.Length);
                if (comparison == 0)
                {
                    comparison = string.Compare(xSignificant, ySignificant, StringComparison.Ordinal);
                }
            }
            else
            {
                comparison = string.Compare(xPart, yPart, StringComparison.OrdinalIgnoreCase);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return xParts.Length != yParts.Length
            ? xParts.Length.CompareTo(yParts.Length)
            : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
