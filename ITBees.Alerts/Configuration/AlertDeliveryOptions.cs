namespace ITBees.Alerts.Configuration;

/// <summary>
/// Engine-wide anti-spam defaults. Registered by <see cref="Setup.AlertsSetup"/> unless the host
/// registered its own instance first.
/// </summary>
public sealed class AlertDeliveryOptions
{
    public static AlertDeliveryOptions Default { get; } = new();

    /// <summary>
    /// Burst digest window for e-mail and SMS. The first alert to an address goes out at once;
    /// anything else for the same address within this window is held and sent as one summary
    /// at its end. A flood (a backlog flushed in one go, one outage seen by several checks) then
    /// costs a recipient two messages instead of dozens. Zero disables it.
    /// </summary>
    public TimeSpan BurstDigestWindow { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Default for <see cref="Catalog.AlertDefinition.FlappingCount"/>.</summary>
    public int DefaultFlappingCount { get; init; } = 3;

    /// <summary>Default for <see cref="Catalog.AlertDefinition.FlappingWindowMinutes"/>.</summary>
    public TimeSpan DefaultFlappingWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Most entries listed in one e-mail digest; the rest is only counted.</summary>
    public int DigestMaxEntries { get; init; } = 50;
}
