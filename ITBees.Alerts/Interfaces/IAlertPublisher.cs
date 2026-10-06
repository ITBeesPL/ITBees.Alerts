using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// The single entry point for producers. Never throws at the caller — an alerting failure
/// must not break the business operation that triggered it.
/// </summary>
public interface IAlertPublisher
{
    Task RaiseAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// The condition behind <paramref name="key"/> for this scope and source has cleared. An alert
    /// still inside its <see cref="Catalog.AlertDefinition.ConfirmationSeconds"/> window is
    /// withdrawn - its occurrence and undelivered deliveries are removed and its cooldown entry
    /// is released. Alerts already delivered are left alone. Cheap to call on every healthy
    /// report: it does nothing for definitions without a confirmation window.
    /// </summary>
    Task ResolveAsync(string key, AlertScope scope, string sourceId, CancellationToken cancellationToken = default);
}
