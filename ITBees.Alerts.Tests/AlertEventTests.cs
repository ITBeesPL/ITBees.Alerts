using System.Globalization;
using ITBees.Alerts.Interfaces;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertEventTests
{
    /// <summary>
    /// The rendered text and the persisted ValuesJson must not change with the host's culture,
    /// or the same threshold reads "92,5" on one deployment and "92.5" on another.
    /// </summary>
    [Test]
    public void Numeric_values_are_formatted_invariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var alertEvent = new AlertEvent("server.disk.full", default)
                .With("value", 92.5d)
                .With("threshold", 90d);

            Assert.Multiple(() =>
            {
                Assert.That(alertEvent.Values["value"], Is.EqualTo("92.5"));
                Assert.That(alertEvent.Values["threshold"], Is.EqualTo("90"));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void Text_values_pass_through_unchanged()
    {
        var alertEvent = new AlertEvent("a.b", default).With("source", "srv-01").With("missing", null);

        Assert.Multiple(() =>
        {
            Assert.That(alertEvent.Values["source"], Is.EqualTo("srv-01"));
            Assert.That(alertEvent.Values["missing"], Is.Null);
        });
    }

    [Test]
    public void Rule_restriction_is_absent_by_default()
    {
        Assert.That(new AlertEvent("a.b", default).RuleGuids, Is.Null,
            "an event-driven producer must reach every matching rule");
    }
}
