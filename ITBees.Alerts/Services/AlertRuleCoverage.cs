using ITBees.Alerts.DbModels;

namespace ITBees.Alerts.Services;

/// <summary>
/// The one answer to "does this rule cover this object?". An empty target list means the rule
/// covers every object of its kind, which is how a platform-wide subscription is expressed.
/// <para>
/// This lived in three places - the publisher's routing, the threshold evaluator's rule pick
/// and the rules listing - and the three had drifted into three different behaviours, so a rule
/// could fire for an object whose settings screen claimed no rule existed.
/// </para>
/// </summary>
internal static class AlertRuleCoverage
{
    public static bool Covers(ICollection<AlertRuleTarget> targets, Guid? scopeId) =>
        targets == null || targets.Count == 0 ||
        (scopeId.HasValue && targets.Any(x => x.ScopeId == scopeId.Value));

    public static bool Covers(AlertRule rule, Guid? scopeId) => Covers(rule?.Targets, scopeId);

    /// <summary>True when the rule names no object and therefore covers the whole scope kind.</summary>
    public static bool IsPlatformWide(ICollection<AlertRuleTarget> targets) =>
        targets == null || targets.Count == 0;
}
