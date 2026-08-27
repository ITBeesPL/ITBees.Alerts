using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// A way of getting an alert to a person. Implementations live in the channel folders and are picked up from DI by
/// <see cref="Services.AlertDeliveryDispatcher"/>; registering none simply means nothing is
/// delivered.
/// </summary>
public interface IAlertChannelSender
{
    AlertChannels Channel { get; }

    Task<AlertDeliveryResult> SendAsync(AlertDeliveryContext context, CancellationToken cancellationToken = default);
}

public class AlertDeliveryContext
{
    public string AlertKey { get; set; }
    public string Discriminator { get; set; }
    public AlertScope Scope { get; set; }
    public AlertSeverity Severity { get; set; }

    /// <summary>E-mail address, phone number, or a scope descriptor for the in-app channel.</summary>
    public string Target { get; set; }

    public string Subject { get; set; }
    public string Body { get; set; }
    public string Link { get; set; }
}

public class AlertDeliveryResult
{
    public bool Success { get; set; }
    public string Error { get; set; }

    public static AlertDeliveryResult Ok() => new() { Success = true };
    public static AlertDeliveryResult Fail(string error) => new() { Success = false, Error = error };
}
