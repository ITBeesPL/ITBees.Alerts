using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// The single entry point for producers. Never throws at the caller — an alerting failure
/// must not break the business operation that triggered it.
/// </summary>
public interface IAlertPublisher
{
    Task RaiseAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default);
}
