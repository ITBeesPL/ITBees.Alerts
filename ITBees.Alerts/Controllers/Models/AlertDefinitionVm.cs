using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using RestVm = ITBees.RestClient.Interfaces.RestModelMarkup.Vm;

namespace ITBees.Alerts.Controllers.Models;

/// <summary>
/// A catalog entry as the settings screen sees it. Display name and description are safe
/// presentation fallbacks; a frontend may still replace them with localized text.
/// </summary>
public class AlertDefinitionVm : RestVm
{

    public AlertDefinitionVm(AlertDefinition definition)
    {
        Key = definition.Key;
        Category = definition.Category;
        DisplayName = definition.DisplayName;
        Description = definition.Description;
        ScopeKind = definition.ScopeKind;
        DefaultSeverity = definition.DefaultSeverity;
        SuggestedChannels = definition.SuggestedChannels;
        MetricKey = definition.MetricKey;
        ValueUnit = definition.ValueUnit;
        DefaultMessage = definition.DefaultTitleTemplate;
        DefaultThrottleMinutes = definition.ThrottleMinutes;
        ConfirmationSeconds = definition.ConfirmationSeconds;
    }

    public string Key { get; set; }
    public string Category { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string ScopeKind { get; set; }
    public AlertSeverity DefaultSeverity { get; set; }
    public AlertChannels SuggestedChannels { get; set; }
    /// <summary>Set for threshold-based alerts; tells the UI to render the parameter inputs.</summary>
    public string MetricKey { get; set; }
    public string ValueUnit { get; set; }

    public string DefaultMessage { get; set; }

    /// <summary>Cooldown a rule inherits when it sets none. Null or 0 means repeats are never suppressed.</summary>
    public int? DefaultThrottleMinutes { get; set; }

    /// <summary>
    /// How long the condition must last before anybody is notified (see AlertDefinition). Shown in
    /// the settings so nobody wonders why a 10-second drop did not page them.
    /// </summary>
    public int? ConfirmationSeconds { get; set; }
}
