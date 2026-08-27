using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.DbModels;

public enum AlertDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>
/// The outbox row. Deliveries are written in the same transaction as the occurrence and
/// picked up by a background dispatcher, so a restart or a second instance cannot lose or
/// duplicate a notification the way an in-memory queue would.
/// </summary>
public class AlertDelivery
{
    public Guid Guid { get; set; }

    /// <summary>Application whose rule produced this delivery.</summary>
    public string Discriminator { get; set; }

    public Guid AlertOccurrenceGuid { get; set; }
    public AlertOccurrence AlertOccurrence { get; set; }

    public Guid? AlertRuleGuid { get; set; }

    /// <summary>The contact that owned the SMS settings when this delivery was queued.</summary>
    public Guid? AlertContactGuid { get; set; }

    /// <summary>Exactly one channel per row — one failing SMS must not block the e-mails.</summary>
    public AlertChannels Channel { get; set; }

    /// <summary>Address, phone number, or a scope descriptor for the in-app channel.</summary>
    public string Target { get; set; }

    public string Subject { get; set; }
    public string Body { get; set; }
    public string Link { get; set; }
    public AlertSeverity Severity { get; set; }

    public AlertDeliveryStatus Status { get; set; } = AlertDeliveryStatus.Pending;
    public string Error { get; set; }

    /// <summary>Earliest dispatch time. Null means the delivery can be sent immediately.</summary>
    public DateTime? NotBeforeUtc { get; set; }

    /// <summary>Marks SMS messages held by contact-level quiet hours.</summary>
    public bool DeferredByQuietHours { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
}
