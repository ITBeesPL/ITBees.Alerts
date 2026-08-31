namespace ITBees.Alerts.DbModels;

/// <summary>
/// One concrete object a rule covers - a parking, a device, whatever the rule's scope kind is.
/// A rule with no targets covers every object of its kind, which is how a platform-wide
/// subscription is expressed; listing targets narrows it to exactly those objects.
/// </summary>
public class AlertRuleTarget
{
    public Guid Guid { get; set; }

    public Guid AlertRuleGuid { get; set; }
    public AlertRule AlertRule { get; set; }

    /// <summary>The object itself, e.g. a parking guid.</summary>
    public Guid ScopeId { get; set; }
}
