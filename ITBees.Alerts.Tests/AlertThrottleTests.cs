using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

/// <summary>
/// The cooldown decides what an operator never hears about, so both halves of the decision are
/// pinned here: how long the window is when several rules disagree, and what counts as "the
/// same alert".
/// </summary>
[TestFixture]
public class AlertThrottleTests
{
    private static AlertDefinition Definition(int? throttleMinutes = null) => new()
    {
        Key = "cash.mdb.error",
        ScopeKind = "parking",
        DefaultTitleTemplate = "Błąd urządzenia MDB - {device}",
        DefaultMessageTemplate = "{message}",
        ThrottleMinutes = throttleMinutes
    };

    private static AlertRule Rule(int? throttleMinutes) => new()
        { Guid = Guid.NewGuid(), ThrottleMinutes = throttleMinutes };

    private static AlertEvent Event(Guid parking, string sourceId) =>
        new("cash.mdb.error", AlertScope.For("parking", parking)) { SourceId = sourceId };

    [Test]
    public void Without_a_window_anywhere_the_cooldown_stays_off()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(null) }, Definition()), Is.Zero);
            Assert.That(AlertThrottle.ResolveMinutes(Array.Empty<AlertRule>(), Definition(60)), Is.Zero,
                "no rule matched, so there is nothing to throttle");
        });
    }

    [Test]
    public void A_rule_inherits_the_catalog_window_and_can_override_it()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(null) }, Definition(60)), Is.EqualTo(60));
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(15) }, Definition(60)), Is.EqualTo(15));
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(0) }, Definition(60)), Is.Zero,
                "0 on the rule turns the inherited cooldown off");
        });
    }

    [Test]
    public void The_shortest_window_wins_so_one_rule_cannot_silence_another()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(240), Rule(15) }, Definition(60)),
                Is.EqualTo(15));
            Assert.That(AlertThrottle.ResolveMinutes(new[] { Rule(240), Rule(0) }, Definition(60)), Is.Zero,
                "a subscription that wants every repeat keeps getting them");
        });
    }

    [Test]
    public void The_same_fault_on_the_same_device_is_one_key()
    {
        var parking = Guid.NewGuid();
        var definition = Definition(60);
        var kasa = Guid.NewGuid().ToString();

        var first = AlertThrottle.BuildKey(definition, Event(parking, kasa), "Błąd MDB - Kasa 0",
            "MDB bill validator error 0x09 – Validator disabled");
        var repeat = AlertThrottle.BuildKey(definition, Event(parking, kasa), "Błąd MDB - Kasa 0",
            "MDB bill validator error 0x09 – Validator disabled");

        Assert.That(repeat, Is.EqualTo(first));
    }

    [Test]
    public void A_different_device_or_a_different_fault_is_a_different_key()
    {
        var parking = Guid.NewGuid();
        var definition = Definition(60);
        var kasa0 = Guid.NewGuid().ToString();
        var kasa1 = Guid.NewGuid().ToString();
        const string title = "Błąd MDB - Kasa";
        const string disabled = "MDB bill validator error 0x09 – Validator disabled";

        var baseline = AlertThrottle.BuildKey(definition, Event(parking, kasa0), title, disabled);

        Assert.Multiple(() =>
        {
            Assert.That(AlertThrottle.BuildKey(definition, Event(parking, kasa1), title, disabled),
                Is.Not.EqualTo(baseline), "a jammed cash point must not silence the one next to it");
            Assert.That(
                AlertThrottle.BuildKey(definition, Event(parking, kasa0), title,
                    "MDB coin device error 0x07 – Tube jam"),
                Is.Not.EqualTo(baseline), "a new fault on the same device still has to get through");
            Assert.That(AlertThrottle.BuildKey(definition, Event(Guid.NewGuid(), kasa0), title, disabled),
                Is.Not.EqualTo(baseline), "another parking is another alert");
        });
    }
}
