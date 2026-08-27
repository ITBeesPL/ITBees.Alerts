using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Supplies a human-readable name for a domain scope. The host application owns the domain
/// model, so the reusable alerting library does not query parking or tenant tables directly.
/// </summary>
public interface IAlertScopeNameResolver
{
    string ResolveName(AlertScope scope);
}
