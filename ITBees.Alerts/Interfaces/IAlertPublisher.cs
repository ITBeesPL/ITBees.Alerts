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
    /// withdrawn for the specified <paramref name="discriminator"/> - its undelivered deliveries
    /// are removed and its cooldown entry is released. The occurrence is removed only when no
    /// other application still references it. Alerts already delivered are left alone. Cheap to
    /// call on every healthy report: it does nothing for definitions without a confirmation window.
    /// </summary>
    Task ResolveAsync(string key, AlertScope scope, string sourceId, string discriminator,
        CancellationToken cancellationToken = default);
}
