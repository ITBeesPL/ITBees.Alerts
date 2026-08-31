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

    /// <summary>
    /// SMS bodies are metered - cut long text on a word boundary and mark the cut. The marker is
    /// plain ASCII on purpose: an ellipsis (U+2026) is outside the GSM 03.38 alphabet, and a
    /// single such character switches the whole message to UCS-2, where one part is 70 characters
    /// instead of 160 - so the provider would bill three parts for the text this cut exists to
    /// keep down to one.
    /// </summary>
    public static string Shorten(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        const string marker = "...";
        if (maxLength <= marker.Length)
            return text.Substring(0, Math.Max(0, maxLength));

        var cut = text.Substring(0, maxLength - marker.Length);
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            cut = cut.Substring(0, lastSpace);

        return cut.TrimEnd() + marker;
    }
}
