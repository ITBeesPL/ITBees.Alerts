using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// What a producer hands to the engine. Producers know nothing about recipients, channels or
/// throttling — they only state that something happened, and to what.
/// </summary>
public class AlertEvent
{
    public AlertEvent() { Values = new Dictionary<string, string>(); }

    public AlertEvent(string key, AlertScope scope) : this()
    {
        Key = key;
        Scope = scope;
    }

    public string Key { get; set; }

    public AlertScope Scope { get; set; }

    /// <summary>Optional owner application restriction, primarily used by test sends.</summary>
    public string Discriminator { get; set; }

    /// <summary>Null falls back to the catalog default.</summary>
    public AlertSeverity? Severity { get; set; }

    public string SourceId { get; set; }
    public string SourceName { get; set; }

    /// <summary>Substituted into the title/message templates and available to threshold rules.</summary>
    public IDictionary<string, string> Values { get; set; }

    /// <summary>Deep link opened from the bell.</summary>
    public string Link { get; set; }

    public AlertEvent With(string name, object value)
    {
        Values[name] = value?.ToString();
        return this;
    }
}
