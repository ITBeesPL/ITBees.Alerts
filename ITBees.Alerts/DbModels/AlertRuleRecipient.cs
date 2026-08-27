using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.DbModels;

/// <summary>
/// Joins a contact to a rule and says which of the rule's channels that person gets — one
/// contact can be on SMS for a dead barrier and on e-mail only for a full disk.
/// </summary>
public class AlertRuleRecipient
{
    public Guid Guid { get; set; }

    public Guid AlertRuleGuid { get; set; }
    public AlertRule AlertRule { get; set; }

    public Guid AlertContactGuid { get; set; }
    public AlertContact AlertContact { get; set; }

    /// <summary>Intersected with the rule's channels at delivery time. InApp is ignored here.</summary>
    public AlertChannels Channels { get; set; } = AlertChannels.Email;
}
