using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using RestIm = ITBees.RestClient.Interfaces.RestModelMarkup.Im;
using RestVm = ITBees.RestClient.Interfaces.RestModelMarkup.Vm;

namespace ITBees.Alerts.Controllers.Models;

/// <summary>One alert as it happened - the history tab and the "what is broken now" filter.</summary>
public class AlertOccurrenceVm : RestVm
{
    public AlertOccurrenceVm() { }

    public AlertOccurrenceVm(AlertOccurrence occurrence)
    {
        Guid = occurrence.Guid;
        AlertKey = occurrence.AlertKey;
        ScopeKind = occurrence.ScopeKind;
        ScopeId = occurrence.ScopeId;
        Severity = occurrence.Severity;
        Title = occurrence.Title;
        Message = occurrence.Message;
        SourceId = occurrence.SourceId;
        SourceName = occurrence.SourceName;
        Link = occurrence.Link;
        CreatedUtc = occurrence.CreatedUtc;
    }

    public Guid Guid { get; set; }
    public string AlertKey { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string SourceId { get; set; }
    public string SourceName { get; set; }
    public string Link { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// One attempt to reach one person on one channel. This is the answer to "did the SMS
/// actually go out?", which today's pipeline cannot give.
/// </summary>
public class AlertDeliveryVm : RestVm
{
    public AlertDeliveryVm() { }

    public AlertDeliveryVm(AlertDelivery delivery)
    {
        Guid = delivery.Guid;
        AlertOccurrenceGuid = delivery.AlertOccurrenceGuid;
        AlertKey = delivery.AlertOccurrence?.AlertKey;
        Channel = delivery.Channel;
        Target = delivery.Target;
        Subject = delivery.Subject;
        Severity = delivery.Severity;
        Status = delivery.Status;
        Error = delivery.Error;
        NotBeforeUtc = delivery.NotBeforeUtc;
        DeferredByQuietHours = delivery.DeferredByQuietHours;
        CreatedUtc = delivery.CreatedUtc;
        SentUtc = delivery.SentUtc;
    }

    public Guid Guid { get; set; }
    public Guid AlertOccurrenceGuid { get; set; }
    public string AlertKey { get; set; }
    public AlertChannels Channel { get; set; }
    public string Target { get; set; }
    public string Subject { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertDeliveryStatus Status { get; set; }
    public string Error { get; set; }
    public DateTime? NotBeforeUtc { get; set; }
    public bool DeferredByQuietHours { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
}

/// <summary>Fires one alert through the configured channels so a rule can be verified before it matters.</summary>
public class AlertTestIm : RestIm
{
    public AlertTestIm() { }

    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
    public string AlertKey { get; set; }
}

public class AlertTestResultVm : RestVm
{
    public AlertTestResultVm() { }

    public bool Success { get; set; }
    public string Message { get; set; }
    public int QueuedDeliveries { get; set; }
}
