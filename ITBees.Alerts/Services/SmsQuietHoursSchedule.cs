using ITBees.Alerts.DbModels;

namespace ITBees.Alerts.Services;

internal static class SmsQuietHoursSchedule
{
    private const string TimeZoneId = "Europe/Warsaw";

    public static DateTime? GetQuietHoursEndUtc(AlertContact contact, DateTime nowUtc)
    {
        if (!contact.SmsQuietHoursEnabled || contact.SmsQuietHoursStartMinute == null ||
            contact.SmsQuietHoursEndMinute == null)
            return null;

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone);
        var currentMinute = localNow.Hour * 60 + localNow.Minute;
        var startMinute = contact.SmsQuietHoursStartMinute.Value;
        var endMinute = contact.SmsQuietHoursEndMinute.Value;
        var isQuiet = startMinute < endMinute
            ? currentMinute >= startMinute && currentMinute < endMinute
            : currentMinute >= startMinute || currentMinute < endMinute;

        if (!isQuiet)
            return null;

        var endDate = localNow.Date;
        if (startMinute > endMinute && currentMinute >= startMinute)
            endDate = endDate.AddDays(1);

        var localEnd = DateTime.SpecifyKind(endDate.AddMinutes(endMinute), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(localEnd))
            localEnd = localEnd.AddMinutes(1);

        return TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone);
    }
}
