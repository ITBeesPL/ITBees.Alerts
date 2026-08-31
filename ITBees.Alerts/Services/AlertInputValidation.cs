#nullable enable
using System.Net.Mail;

namespace ITBees.Alerts.Services;

internal static class AlertInputValidation
{
    internal static bool IsEmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320 ||
            value.Contains('\r') || value.Contains('\n'))
            return false;

        var address = value.Trim();
        return MailAddress.TryCreate(address, out var parsed) &&
               string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsWebLink(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
