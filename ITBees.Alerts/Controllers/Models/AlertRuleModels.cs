using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using RestDm = ITBees.RestClient.Interfaces.RestModelMarkup.Dm;
using RestIm = ITBees.RestClient.Interfaces.RestModelMarkup.Im;
using RestUm = ITBees.RestClient.Interfaces.RestModelMarkup.Um;
using RestVm = ITBees.RestClient.Interfaces.RestModelMarkup.Vm;

namespace ITBees.Alerts.Controllers.Models;

/// <summary>One row of the notifications settings screen: an alert kind plus how it is routed.</summary>
public class AlertRuleVm : RestVm
{
    public AlertRuleVm() { Recipients = new List<AlertRuleRecipientVm>(); ScopeIds = new List<Guid>(); }

    public AlertRuleVm(AlertRule rule)
    {
        Guid = rule.Guid;
        Discriminator = rule.Discriminator;
        ScopeKind = rule.ScopeKind;
        AlertKey = rule.AlertKey;
        ScopeIds = (rule.Targets ?? new List<AlertRuleTarget>()).Select(x => x.ScopeId).ToList();
        IsPlatformRule = rule.IsPlatformRule;
        Enabled = rule.Enabled;
        Channels = rule.Channels;
        MinSeverity = rule.MinSeverity;
        Description = rule.Description;
        ComparisonOperator = rule.ComparisonOperator;
        ComparisonValue = rule.ComparisonValue;
        CustomMessage = rule.CustomMessage;
        Recipients = (rule.Recipients ?? new List<AlertRuleRecipient>())
            .Select(x => new AlertRuleRecipientVm(x)).ToList();
    }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }

    /// <summary>
    /// Objects the rule covers. Empty means every object of the scope kind - a platform-wide
    /// subscription.
    /// </summary>
    public List<Guid> ScopeIds { get; set; }

    public string AlertKey { get; set; }
    public bool IsPlatformRule { get; set; }
    public bool Enabled { get; set; }
    public AlertChannels Channels { get; set; }
    public AlertSeverity MinSeverity { get; set; }
    public string Description { get; set; }
    public AlertComparisonOperator ComparisonOperator { get; set; }
    public double? ComparisonValue { get; set; }
    public string CustomMessage { get; set; }
    public List<AlertRuleRecipientVm> Recipients { get; set; }
}

public class AlertRuleRecipientVm : RestVm
{
    public AlertRuleRecipientVm() { }

    public AlertRuleRecipientVm(AlertRuleRecipient recipient)
    {
        Guid = recipient.Guid;
        AlertContactGuid = recipient.AlertContactGuid;
        Channels = recipient.Channels;
        Name = recipient.AlertContact?.Name;
        Email = recipient.AlertContact?.Email;
        Phone = recipient.AlertContact?.Phone;
    }

    public Guid Guid { get; set; }
    public Guid AlertContactGuid { get; set; }
    public AlertChannels Channels { get; set; }

    /// <summary>Copied from the contact so the settings screen renders in one request.</summary>
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
}

public class AlertRuleRecipientIm : RestIm
{
    public AlertRuleRecipientIm() { }

    public Guid AlertContactGuid { get; set; }
    public AlertChannels Channels { get; set; }
}

public class AlertRuleIm : RestIm
{
    public AlertRuleIm() { Recipients = new List<AlertRuleRecipientIm>(); ScopeIds = new List<Guid>(); }

    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }

    /// <summary>Objects the rule covers. Empty means every object of the scope kind.</summary>
    public List<Guid> ScopeIds { get; set; }
    public string AlertKey { get; set; }
    public bool Enabled { get; set; } = true;
    public AlertChannels Channels { get; set; }
    public AlertSeverity MinSeverity { get; set; }
    public string Description { get; set; }
    public AlertComparisonOperator ComparisonOperator { get; set; }
    public double? ComparisonValue { get; set; }
    public string CustomMessage { get; set; }
    public List<AlertRuleRecipientIm> Recipients { get; set; }
}

public class AlertRuleUm : RestUm
{
    public AlertRuleUm() { Recipients = new List<AlertRuleRecipientIm>(); ScopeIds = new List<Guid>(); }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }
    public string AlertKey { get; set; }

    /// <summary>Replaces the whole target list. Empty means every object of the scope kind.</summary>
    public List<Guid> ScopeIds { get; set; }
    public bool Enabled { get; set; } = true;
    public AlertChannels Channels { get; set; }
    public AlertSeverity MinSeverity { get; set; }
    public string Description { get; set; }
    public AlertComparisonOperator ComparisonOperator { get; set; }
    public double? ComparisonValue { get; set; }
    public string CustomMessage { get; set; }

    /// <summary>Replaces the whole recipient list - the screen always sends the full set.</summary>
    public List<AlertRuleRecipientIm> Recipients { get; set; }
}

public class AlertRuleDm : RestDm
{
    public AlertRuleDm() { }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }
}
