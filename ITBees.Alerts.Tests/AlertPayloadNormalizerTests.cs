using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Interfaces;
using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

/// <summary>
/// An oversized payload used to throw out of a never-throwing entry point, so the whole alert
/// was silently lost. These pin the replacement: shorten, never discard the alert.
/// </summary>
[TestFixture]
public class AlertPayloadNormalizerTests
{
    private static AlertEvent Event() => new("device.connection.lost", AlertScope.Global);

    [Test]
    public void An_oversized_value_is_shortened_rather_than_rejected()
    {
        var alertEvent = Event();
        alertEvent.Values["detail"] = new string('x', AlertContentLimits.Body * 2);

        var dropped = AlertPayloadNormalizer.Normalize(alertEvent);

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.Zero);
            Assert.That(alertEvent.Values, Does.ContainKey("detail"), "the alert must survive");
            Assert.That(alertEvent.Values["detail"]!.Length, Is.LessThanOrEqualTo(AlertContentLimits.Body));
        });
    }

    [Test]
    public void Oversized_scalars_are_shortened()
    {
        var alertEvent = Event();
        alertEvent.Link = "https://example.com/" + new string('a', AlertContentLimits.Link * 2);
        alertEvent.SourceId = new string('b', AlertContentLimits.SourceId * 2);
        alertEvent.SourceName = new string('c', AlertContentLimits.SourceName * 2);

        AlertPayloadNormalizer.Normalize(alertEvent);

        Assert.Multiple(() =>
        {
            Assert.That(alertEvent.Link!.Length, Is.LessThanOrEqualTo(AlertContentLimits.Link));
            Assert.That(alertEvent.SourceId!.Length, Is.LessThanOrEqualTo(AlertContentLimits.SourceId));
            Assert.That(alertEvent.SourceName!.Length, Is.LessThanOrEqualTo(AlertContentLimits.SourceName));
        });
    }

    [Test]
    public void An_unusable_key_is_dropped_rather_than_cut()
    {
        var alertEvent = Event();
        var longKey = new string('k', AlertContentLimits.ValueKey + 1);
        alertEvent.Values[longKey] = "value";
        alertEvent.Values["good"] = "value";

        var dropped = AlertPayloadNormalizer.Normalize(alertEvent);

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.EqualTo(1));
            Assert.That(alertEvent.Values.Keys, Is.EquivalentTo(new[] { "good" }),
                "a cut key would silently stop matching its placeholder");
        });
    }

    [Test]
    public void The_entry_count_is_capped_and_the_overflow_reported()
    {
        var alertEvent = Event();
        for (var i = 0; i < AlertContentLimits.ValueEntries + 7; i++)
            alertEvent.Values[$"k{i}"] = "v";

        var dropped = AlertPayloadNormalizer.Normalize(alertEvent);

        Assert.Multiple(() =>
        {
            Assert.That(alertEvent.Values, Has.Count.EqualTo(AlertContentLimits.ValueEntries));
            Assert.That(dropped, Is.EqualTo(7));
        });
    }

    [Test]
    public void A_payload_within_the_limits_is_left_alone()
    {
        var alertEvent = Event();
        alertEvent.Link = "https://example.com/a";
        alertEvent.SourceName = "srv-01";
        alertEvent.Values["value"] = "95";

        var dropped = AlertPayloadNormalizer.Normalize(alertEvent);

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.Zero);
            Assert.That(alertEvent.Link, Is.EqualTo("https://example.com/a"));
            Assert.That(alertEvent.SourceName, Is.EqualTo("srv-01"));
            Assert.That(alertEvent.Values["value"], Is.EqualTo("95"));
        });
    }

    [Test]
    public void A_null_value_collection_is_tolerated()
    {
        var alertEvent = Event();
        alertEvent.Values = null!;

        Assert.DoesNotThrow(() => AlertPayloadNormalizer.Normalize(alertEvent));
        Assert.That(alertEvent.Values, Is.Empty);
    }
}
