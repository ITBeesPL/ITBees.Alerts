using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Feeds the threshold evaluator. One reading per object being watched — a source for
/// "server.disk.used_percent" returns a single global reading, a source for
/// "device.cpu.percent" returns one per device.
/// </summary>
public interface IAlertMetricSource
{
    bool Handles(string metricKey);

    Task<IReadOnlyCollection<AlertMetricReading>> ReadAsync(string metricKey,
        CancellationToken cancellationToken = default);
}

public class AlertMetricReading
{
    public AlertScope Scope { get; set; }
    public double Value { get; set; }
    public string SourceId { get; set; }
    public string SourceName { get; set; }
    public DateTime TimestampUtc { get; set; }
}
