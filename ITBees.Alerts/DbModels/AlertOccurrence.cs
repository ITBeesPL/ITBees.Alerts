using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.DbModels;

/// <summary>
/// One alert as it actually happened.
/// </summary>
public class AlertOccurrence
{
    public Guid Guid { get; set; }

    public string AlertKey { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }

    public AlertSeverity Severity { get; set; }

    public string Title { get; set; }
    public string Message { get; set; }

    /// <summary>Free-form origin, e.g. the device guid. Only used for display and grouping.</summary>
    public string SourceId { get; set; }
    public string SourceName { get; set; }

    public string Link { get; set; }
    public string ValuesJson { get; set; }

    public DateTime CreatedUtc { get; set; }
}
