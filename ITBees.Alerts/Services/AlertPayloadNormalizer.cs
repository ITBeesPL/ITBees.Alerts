using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Interfaces;

namespace ITBees.Alerts.Services;

/// <summary>
/// Trims an oversized payload to the documented limits instead of rejecting it.
/// <para>
/// Rejecting threw out of <see cref="IAlertPublisher"/>, which is documented never to throw at
/// the caller, so the exception was swallowed and one long diagnostic value silently destroyed
/// the whole alert - while the title and message on the very same path were already being cut.
/// An alert that arrives shortened beats an alert that never arrives.
/// </para>
/// </summary>
internal static class AlertPayloadNormalizer
{
    /// <summary>Trims the event in place and returns how many values had to be dropped entirely.</summary>
    public static int Normalize(AlertEvent alertEvent)
    {
        alertEvent.Link = AlertTemplateRenderer.Shorten(alertEvent.Link, AlertContentLimits.Link);
        alertEvent.SourceId = AlertTemplateRenderer.Shorten(alertEvent.SourceId, AlertContentLimits.SourceId);
        alertEvent.SourceName = AlertTemplateRenderer.Shorten(alertEvent.SourceName, AlertContentLimits.SourceName);

        alertEvent.Values ??= new Dictionary<string, string>();

        var normalized = new Dictionary<string, string>();
        var dropped = 0;
        foreach (var pair in alertEvent.Values)
        {
            // A key is a template placeholder name. Cutting it would leave a placeholder that
            // silently stops matching, so an unusable key is dropped rather than mangled.
            if (string.IsNullOrEmpty(pair.Key) || pair.Key.Length > AlertContentLimits.ValueKey ||
                normalized.Count >= AlertContentLimits.ValueEntries)
            {
                dropped++;
                continue;
            }

            normalized[pair.Key] = AlertTemplateRenderer.Shorten(pair.Value, AlertContentLimits.Body);
        }

        alertEvent.Values = normalized;
        return dropped;
    }
}
