using ITBees.Alerts.DbModels;
using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

/// <summary>
/// Coverage decides both who gets alerted and which rules an operator is shown, and the two used
/// to disagree - so the rule is pinned here once.
/// </summary>
[TestFixture]
public class AlertRuleCoverageTests
{
    private static AlertRule Rule(params Guid[] targets) => new()
    {
        Guid = Guid.NewGuid(),
        Targets = targets.Select(x => new AlertRuleTarget { Guid = Guid.NewGuid(), ScopeId = x }).ToList()
    };

    [Test]
    public void A_rule_without_targets_covers_every_object_of_its_kind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertRuleCoverage.Covers(Rule(), Guid.NewGuid()), Is.True);
            Assert.That(AlertRuleCoverage.Covers(new AlertRule(), Guid.NewGuid()), Is.True,
                "a null target collection means the same as an empty one");
        });
    }

    [Test]
    public void A_targeted_rule_covers_only_the_objects_it_lists()
    {
        var parking = Guid.NewGuid();
        var rule = Rule(parking);

        Assert.Multiple(() =>
        {
            Assert.That(AlertRuleCoverage.Covers(rule, parking), Is.True);
            Assert.That(AlertRuleCoverage.Covers(rule, Guid.NewGuid()), Is.False);
        });
    }

    [Test]
    public void Only_an_untargeted_rule_covers_a_scope_without_an_id()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertRuleCoverage.Covers(Rule(), null), Is.True);
            Assert.That(AlertRuleCoverage.Covers(Rule(Guid.NewGuid()), null), Is.False,
                "a targeted rule must not match the whole-kind scope");
        });
    }

    [Test]
    public void Platform_wide_is_exactly_the_absence_of_targets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertRuleCoverage.IsPlatformWide(Rule().Targets), Is.True);
            Assert.That(AlertRuleCoverage.IsPlatformWide(null), Is.True);
            Assert.That(AlertRuleCoverage.IsPlatformWide(Rule(Guid.NewGuid()).Targets), Is.False);
        });
    }
}
