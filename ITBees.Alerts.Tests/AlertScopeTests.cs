using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Interfaces;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertScopeTests
{
    [Test]
    public void A_blank_kind_normalises_to_global()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new AlertScope(null).Kind, Is.EqualTo(AlertScope.GlobalKind));
            Assert.That(new AlertScope("").Kind, Is.EqualTo(AlertScope.GlobalKind));
            Assert.That(new AlertScope("   ").Kind, Is.EqualTo(AlertScope.GlobalKind));
        });
    }

    [Test]
    public void A_kind_is_trimmed_and_lowercased()
    {
        Assert.That(new AlertScope("  Parking ").Kind, Is.EqualTo("parking"));
    }

    /// <summary>
    /// AlertScope is a struct, and its normalisation lives in the constructor. Anything reaching
    /// the engine through an object initialiser (AlertMetricReading.Scope, AlertEvent.Scope) or
    /// through default(AlertScope) skips it, and a null Kind matches no rule at all - silently.
    /// </summary>
    [Test]
    public void A_default_scope_still_reports_the_global_kind()
    {
        var scope = default(AlertScope);

        Assert.That(scope.Kind, Is.EqualTo(AlertScope.GlobalKind),
            "a default scope with a null Kind silently matches no rule");
    }

    [Test]
    public void A_default_metric_reading_carries_a_usable_scope()
    {
        var reading = new AlertMetricReading { Value = 95 };

        Assert.That(reading.Scope.Kind, Is.EqualTo(AlertScope.GlobalKind),
            "the documented 'single global reading' implementation must not produce a null Kind");
    }

    [Test]
    public void Scopes_compare_by_kind_and_id()
    {
        var id = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(AlertScope.For("parking", id), Is.EqualTo(AlertScope.For("parking", id)));
            Assert.That(AlertScope.For("parking", id), Is.Not.EqualTo(AlertScope.For("parking", Guid.NewGuid())));
            Assert.That(new AlertScope("parking"), Is.Not.EqualTo(AlertScope.For("parking", id)));
            Assert.That(AlertScope.For("PARKING", id), Is.EqualTo(AlertScope.For("parking", id)),
                "kind normalisation makes the comparison case-insensitive");
        });
    }

    [Test]
    public void ToString_renders_kind_and_id()
    {
        var id = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(AlertScope.Global.ToString(), Is.EqualTo("global"));
            Assert.That(AlertScope.For("parking", id).ToString(), Is.EqualTo($"parking:{id}"));
        });
    }

    [Test]
    public void A_blank_contact_book_kind_normalises_to_platform()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new AlertContactBook(null).Kind, Is.EqualTo(AlertContactBook.PlatformKind));
            Assert.That(new AlertContactBook(" Company ").Kind, Is.EqualTo("company"));
            Assert.That(default(AlertContactBook).Kind, Is.EqualTo(AlertContactBook.PlatformKind));
        });
    }
}
