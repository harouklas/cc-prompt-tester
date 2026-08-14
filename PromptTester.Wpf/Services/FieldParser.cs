namespace PromptTester.Wpf.Services;

public static class FieldParser
{
    public static IReadOnlyList<string> Parse(string rawFields)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in rawFields.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var field = item.Trim();
            if (field.Length == 0 || !seen.Add(field))
            {
                continue;
            }

            fields.Add(field);
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException("Add at least one field to extract.");
        }

        var overlyLongField = fields.FirstOrDefault(field => field.Length > 128);
        if (overlyLongField is not null)
        {
            throw new InvalidOperationException($"Field names must be 128 characters or fewer: {overlyLongField}");
        }

        return fields;
    }
}
