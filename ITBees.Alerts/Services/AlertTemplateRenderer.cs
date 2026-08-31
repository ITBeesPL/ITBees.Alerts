using System.Text;

namespace ITBees.Alerts.Services;

/// <summary>
/// Fills <c>{placeholder}</c> holes in a title or message from the event values. Unknown
/// placeholders are left as-is so a typo in a template is visible instead of silently
/// producing an empty sentence.
/// </summary>
public static class AlertTemplateRenderer
{
    public static string Render(string template, IDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template) || values == null || values.Count == 0)
            return template;

        var result = new StringBuilder(template);
        foreach (var pair in values)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;
            result.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
        }

        return result.ToString();
    }

    /// <summary>SMS bodies are metered — cut long text on a word boundary and mark the cut.</summary>
    public static string Shorten(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        var cut = text.Substring(0, maxLength - 1);
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            cut = cut.Substring(0, lastSpace);

        return cut.TrimEnd() + "…";
    }
}
