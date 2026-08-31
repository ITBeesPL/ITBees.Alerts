using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Answers "may the current user configure alerts for this scope?". Implemented by the host
/// application, because only it knows what a parking, a company or a platform role is.
/// Throw from the implementation to deny — the REST layer turns that into an error response.
/// </summary>
public interface IAlertScopeAuthorization
{
    /// <summary>Read access to rules, contacts and history of the scope.</summary>
    void CheckRead(AlertScope scope);

    /// <summary>Write access — creating rules and contacts.</summary>
    void CheckWrite(AlertScope scope);

    /// <summary>
    /// True when the current user administers the platform. Platform rules (all parkings at
    /// once, global alerts) are only editable by them.
    /// </summary>
    bool IsPlatformAdministrator();
}
