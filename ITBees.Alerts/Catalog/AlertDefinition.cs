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

    /// <summary>Fallback text used when a rule has no custom message. Supports <c>{placeholder}</c> from the event values.</summary>
    public string DefaultTitleTemplate { get; set; }

    public string DefaultMessageTemplate { get; set; }
}
