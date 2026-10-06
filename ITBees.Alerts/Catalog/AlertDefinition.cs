using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Catalog;

/// <summary>
/// One entry of the alert catalog. Definitions live in code and are contributed by modules
/// through <see cref="IAlertCatalogSource"/> — adding an alert kind never means a schema
/// migration, because <see cref="Key"/> is a string rather than an enum value.
/// <para>
/// The user-facing label is deliberately absent: the frontend resolves it from
/// <see cref="Key"/> through i18n, the same way the operator notifications screen already
/// does it today.
/// </para>
/// </summary>
public class AlertDefinition
{

    /// <summary>Stable dotted identifier, e.g. <c>device.connection.lost</c>.</summary>
    public string Key { get; set; }

    /// <summary>Grouping for the settings screen, e.g. <c>device</c>, <c>cash</c>, <c>server</c>.</summary>
    public string Category { get; set; }

    /// <summary>Human-readable fallback name shown by configuration clients.</summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Human-readable explanation of the condition. Unlike delivery templates, it must not
    /// contain runtime placeholders such as <c>{device}</c> or <c>{value}</c>.
    /// </summary>
    public string Description { get; set; }

    /// <summary>Scope kind this alert is raised for — "parking", "global", ...</summary>
    public string ScopeKind { get; set; } = AlertScope.GlobalKind;

    public AlertSeverity DefaultSeverity { get; set; } = AlertSeverity.Warning;

    /// <summary>Channels pre-ticked when somebody creates a rule for this alert. Not a default rule — no rules are seeded.</summary>
    public AlertChannels SuggestedChannels { get; set; } = AlertChannels.InApp;

    /// <summary>
    /// Metric this alert is evaluated from, when it is threshold-based rather than event-based
    /// (e.g. <c>server.disk.used_percent</c>). Null for event-driven alerts.
    /// </summary>
    public string MetricKey { get; set; }

    /// <summary>Unit displayed next to the numeric comparison value, e.g. %, MB/s.</summary>
    public string ValueUnit { get; set; }

    /// <summary>
    /// How long the same alert (same scope, same source, same content) stays silent after one
    /// was delivered. Null or 0 means no cooldown — every repeat is recorded and delivered,
    /// which is the historical behaviour and stays the default for every alert that does not
    /// opt in. A rule may override this per subscription.
    /// </summary>
    public int? ThrottleMinutes { get; set; }

    /// <summary>
    /// How long a condition must last before anybody is told about it. Null or 0 delivers at
    /// once - right for discrete events (a lost payment, a failed print) that do not "clear".
    /// <para>
    /// Event-driven alerts are recorded immediately, but their deliveries wait out this window;
    /// a producer that sees the condition clear calls <see cref="Interfaces.IAlertPublisher.ResolveAsync"/>
    /// and the alert is withdrawn as if it never happened - a half-second network blip must not
    /// page anybody. Threshold (metric) alerts instead require the comparison to hold on every
    /// evaluation for this long.
    /// </para>
    /// </summary>
    public int? ConfirmationSeconds { get; set; }

    /// <summary>
    /// Flapping guard for <see cref="ConfirmationSeconds"/>: after this many alerts were withdrawn
    /// within <see cref="FlappingWindowMinutes"/>, the next one is delivered at once and says the
    /// condition is unstable - a link that drops for 10 s every few minutes is a real problem even
    /// though no single drop lasts long enough. Defaults: 3 withdrawals in 30 minutes.
    /// </summary>
    public int? FlappingCount { get; set; }

    public int? FlappingWindowMinutes { get; set; }

    /// <summary>Fallback text used when a rule has no custom message. Supports <c>{placeholder}</c> from the event values.</summary>
    public string DefaultTitleTemplate { get; set; }

    public string DefaultMessageTemplate { get; set; }
}
