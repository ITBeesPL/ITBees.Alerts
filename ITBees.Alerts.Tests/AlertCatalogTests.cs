using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertCatalogTests
{
    private sealed class Source(params AlertDefinition[] definitions) : IAlertCatalogSource
    {
        public IEnumerable<AlertDefinition> GetDefinitions() => definitions;
    }

    private static AlertDefinition Definition(string key, string scopeKind = "parking",
        string category = "device") => new() { Key = key, ScopeKind = scopeKind, Category = category };

    private static AlertCatalog Build(params IAlertCatalogSource[] sources) =>
        new(sources, NullLogger<AlertCatalog>.Instance);

    [Test]
    public void Definitions_from_every_source_are_aggregated()
    {
        var catalog = Build(new Source(Definition("a.b")), new Source(Definition("c.d")));

        Assert.That(catalog.All.Select(x => x.Key), Is.EquivalentTo(new[] { "a.b", "c.d" }));
    }

    [Test]
    public void A_duplicate_key_is_dropped_instead_of_throwing()
    {
        var catalog = Build(new Source(Definition("a.b"), Definition("a.b")));

        Assert.That(catalog.All, Has.Count.EqualTo(1), "a misbehaving module must not stop the host booting");
    }

    [Test]
    public void A_definition_without_a_key_is_skipped()
    {
        var catalog = Build(new Source(Definition("a.b"), Definition("  "), new AlertDefinition()));

        Assert.That(catalog.All.Select(x => x.Key), Is.EquivalentTo(new[] { "a.b" }));
    }

    [Test]
    public void Lookup_is_case_insensitive_and_null_safe()
    {
        var catalog = Build(new Source(Definition("Device.Connection.Lost")));

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Get("device.connection.lost"), Is.Not.Null);
            Assert.That(catalog.Get("nope"), Is.Null);
            Assert.That(catalog.Get(null!), Is.Null);
        });
    }

    /// <summary>
    /// ForScopeKind documents null as "everything for admin", and the controller declares the
    /// query parameter optional - so the whole-catalog listing has to be reachable.
    /// </summary>
    [Test]
    public void A_null_scope_kind_returns_the_whole_catalog()
    {
        var catalog = Build(new Source(Definition("a.b", "parking"), Definition("c.d", "global")));

        Assert.That(catalog.ForScopeKind(null!), Has.Count.EqualTo(2));
    }

    [Test]
    public void A_scope_kind_filters_case_insensitively()
    {
        var catalog = Build(new Source(Definition("a.b", "parking"), Definition("c.d", "global")));

        Assert.That(catalog.ForScopeKind("PARKING").Select(x => x.Key), Is.EquivalentTo(new[] { "a.b" }));
    }
}

[TestFixture]
public class AlertSeverityTests
{
    /// <summary>
    /// Rules subscribe with a minimum severity and the publisher filters on `severity >= MinSeverity`,
    /// so the numeric order of this enum is load-bearing.
    /// </summary>
    [Test]
    public void Severities_are_ordered_from_least_to_most_severe()
    {
        Assert.That(new[]
        {
            AlertSeverity.Info, AlertSeverity.Warning, AlertSeverity.Error, AlertSeverity.Critical
        }, Is.Ordered.Ascending);
    }

    [Test]
    public void A_minimum_severity_hears_everything_above_it()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertSeverity.Critical >= AlertSeverity.Warning, Is.True);
            Assert.That(AlertSeverity.Info >= AlertSeverity.Warning, Is.False);
        });
    }
}
