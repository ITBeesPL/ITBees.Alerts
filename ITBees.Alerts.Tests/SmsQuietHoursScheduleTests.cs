using ITBees.Alerts.DbModels;
using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

/// <summary>
/// Quiet hours decide whether somebody's phone rings at 3am, so the window arithmetic is
/// pinned here: overnight windows crossing midnight, same-day windows, and the boundaries.
/// </summary>
[TestFixture]
public class SmsQuietHoursScheduleTests
{
    private static AlertContact Contact(bool enabled, int? startMinute, int? endMinute) => new()
    {
        Guid = Guid.NewGuid(),
        SmsQuietHoursEnabled = enabled,
        SmsQuietHoursStartMinute = startMinute,
        SmsQuietHoursEndMinute = endMinute
    };

    /// <summary>Local Warsaw wall-clock time expressed as UTC, so the tests read in local terms.</summary>
    private static DateTime WarsawLocalAsUtc(int year, int month, int day, int hour, int minute)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static DateTime ToWarsawLocal(DateTime utc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
    }

    [Test]
    public void Disabled_quiet_hours_never_defer()
    {
        var contact = Contact(false, 22 * 60, 6 * 60);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 10, 23, 0));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Enabled_but_unconfigured_window_never_defers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmsQuietHoursSchedule.GetQuietHoursEndUtc(
                Contact(true, null, 6 * 60), WarsawLocalAsUtc(2026, 3, 10, 23, 0)), Is.Null);
            Assert.That(SmsQuietHoursSchedule.GetQuietHoursEndUtc(
                Contact(true, 22 * 60, null), WarsawLocalAsUtc(2026, 3, 10, 23, 0)), Is.Null);
        });
    }

    [Test]
    public void Outside_an_overnight_window_nothing_is_deferred()
    {
        var contact = Contact(true, 22 * 60, 6 * 60);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 10, 12, 0));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Before_midnight_inside_an_overnight_window_defers_to_the_next_morning()
    {
        var contact = Contact(true, 22 * 60, 6 * 60);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 10, 23, 0));

        Assert.That(result, Is.Not.Null);
        var local = ToWarsawLocal(result!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(local.Date, Is.EqualTo(new DateTime(2026, 3, 11)), "must roll over to the next day");
            Assert.That(local.TimeOfDay, Is.EqualTo(TimeSpan.FromHours(6)));
        });
    }

    [Test]
    public void After_midnight_inside_an_overnight_window_defers_to_the_same_morning()
    {
        var contact = Contact(true, 22 * 60, 6 * 60);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 11, 2, 0));

        Assert.That(result, Is.Not.Null);
        var local = ToWarsawLocal(result!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(local.Date, Is.EqualTo(new DateTime(2026, 3, 11)), "must NOT roll over - the window ends today");
            Assert.That(local.TimeOfDay, Is.EqualTo(TimeSpan.FromHours(6)));
        });
    }

    [Test]
    public void A_same_day_window_defers_to_its_end_on_that_day()
    {
        var contact = Contact(true, 9 * 60, 17 * 60);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 10, 12, 0));

        Assert.That(result, Is.Not.Null);
        var local = ToWarsawLocal(result!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(local.Date, Is.EqualTo(new DateTime(2026, 3, 10)));
            Assert.That(local.TimeOfDay, Is.EqualTo(TimeSpan.FromHours(17)));
        });
    }

    [Test]
    public void The_window_start_is_inclusive_and_the_end_is_exclusive()
    {
        var contact = Contact(true, 22 * 60, 6 * 60);

        Assert.Multiple(() =>
        {
            Assert.That(SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 10, 22, 0)),
                Is.Not.Null, "start minute is inside the window");
            Assert.That(SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 11, 6, 0)),
                Is.Null, "end minute is already outside the window");
        });
    }

    /// <summary>
    /// Poland springs forward at 02:00 local on the last Sunday of March, so 02:30 does not exist
    /// that night. A window ending inside the gap must still resolve to a real instant.
    /// </summary>
    [Test]
    public void A_window_ending_in_the_spring_forward_gap_still_resolves()
    {
        var contact = Contact(true, 22 * 60, 2 * 60 + 30);

        var result = SmsQuietHoursSchedule.GetQuietHoursEndUtc(contact, WarsawLocalAsUtc(2026, 3, 28, 23, 0));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Kind, Is.Not.EqualTo(DateTimeKind.Unspecified));
    }
}
