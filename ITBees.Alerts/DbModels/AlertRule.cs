using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.DbModels;

/// <summary>
/// "Notify me about X, this way." Nothing is seeded — operators and administrators create their
/// own rules, so an alert with no rule is recorded nowhere and delivered to nobody.
/// <para>
/// Scope semantics: <see cref="ScopeKind"/> says what kind of object the rule is about, and
/// <see cref="Targets"/> says which ones. An empty target list means "every object of that
/// kind" — that is how an administrator subscribes to a parking-scoped alert across all
/// parkings at once; listing targets narrows the same rule to a handful of parkings.
/// </para>
/// </summary>
public class AlertRule
{
    public Guid Guid { get; set; }

    /// <summary>Host application that owns and manages this rule.</summary>
    public string Discriminator { get; set; }

    /// <summary>Matches <see cref="Catalog.AlertDefinition.ScopeKind"/>.</summary>
    public string ScopeKind { get; set; }

    public string AlertKey { get; set; }

    /// <summary>
    /// True when the rule was created in the administration panel. Operators only ever see and
    /// edit rules targeting their own parkings, so a platform-wide rule cannot be switched off
    /// from below.
    /// </summary>
    public bool IsPlatformRule { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Which channels fire for this alert. Contacts additionally narrow e-mail/SMS per person.</summary>
    public AlertChannels Channels { get; set; } = AlertChannels.InApp;

    public AlertSeverity MinSeverity { get; set; } = AlertSeverity.Warning;

    /// <summary>Optional user description shown on the configured-alerts list.</summary>
    public string Description { get; set; }

    /// <summary>Condition configured by the user for metric-backed alert types.</summary>
    public AlertComparisonOperator ComparisonOperator { get; set; }

    public double? ComparisonValue { get; set; }

    /// <summary>Operator-authored headline replacing the catalog template. Optional.</summary>
    public string CustomMessage { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool Deleted { get; set; }

    public ICollection<AlertRuleRecipient> Recipients { get; set; }

    /// <summary>Objects this rule covers. Empty means every object of <see cref="ScopeKind"/>.</summary>
    public ICollection<AlertRuleTarget> Targets { get; set; }
}
