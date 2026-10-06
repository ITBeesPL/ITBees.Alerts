using System.Globalization;
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

    /// <summary>
    /// Restricts delivery to rules of this application. Null intentionally broadcasts to matching
    /// rules of ALL applications sharing the database. Only trusted internal producers may use
    /// null; user-triggered events must resolve the discriminator through IAlertContext.
    /// </summary>
    public string Discriminator { get; set; }

    /// <summary>Null falls back to the catalog default.</summary>
    public AlertSeverity? Severity { get; set; }

    public string SourceId { get; set; }
    public string SourceName { get; set; }

    /// <summary>Substituted into the title/message templates and available to threshold rules.</summary>
    public IDictionary<string, string> Values { get; set; }

    /// <summary>Deep link opened from the bell.</summary>
    public string Link { get; set; }

    /// <summary>
    /// Skips the cooldown and the burst digest. Set it for events a human explicitly asked for - a test send has
    /// identical content every time, so without this the second test in a window would vanish
    /// and the screen would report that nothing was sent.
    /// </summary>
    public bool IgnoreThrottle { get; set; }

    /// <summary>
    /// Delivers at once even when the definition has <see cref="Catalog.AlertDefinition.ConfirmationSeconds"/>.
    /// For producers that already confirmed the condition themselves (the threshold evaluator
    /// requires it to hold for the whole window) and for test sends.
    /// </summary>
    public bool SkipConfirmation { get; set; }

    /// <summary>
    /// Restricts delivery to these rules. The threshold evaluator sets it, because it has already
    /// decided which subscriptions crossed their own comparison value and must not let the
    /// publisher re-match every other rule of the same alert kind. Null - the normal case for an
    /// event-driven producer - lets every matching rule fire.
    /// </summary>
    public IReadOnlyCollection<Guid> RuleGuids { get; set; }

    /// <summary>
    /// Numbers are formatted invariantly on purpose: the rendered text and the persisted
    /// ValuesJson must not change meaning with the host's thread culture (a threshold would read
    /// "92,5" on one deployment and "92.5" on another, and the same alert would serialise
    /// differently). Display localisation belongs to the frontend, which resolves labels by key.
    /// </summary>
    public AlertEvent With(string name, object value)
    {
        Values[name] = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString();
        return this;
    }
}
