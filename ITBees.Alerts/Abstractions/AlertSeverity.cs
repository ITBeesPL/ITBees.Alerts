namespace ITBees.Alerts.Abstractions;

/// <summary>
/// How bad an alert is. Ordered so a rule can subscribe with a minimum severity
/// (<c>MinSeverity = Warning</c> hears Warning and everything above it).
/// </summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}
