namespace ITBees.Alerts.DbModels;

/// <summary>
/// Cooldown bookkeeping: one row per "the same alert, about the same thing, with the same
/// content". While a row is inside its window the engine records nothing and delivers nothing —
/// it only counts what it swallowed.
/// <para>
/// The state lives in the database rather than in process memory on purpose: applications
/// sharing one database run as separate processes (admin api, operator api, ...), and an
/// in-memory cooldown would let each of them send its own copy of the same alert and would
/// reset on every deployment.
/// </para>
/// </summary>
public class AlertThrottleState
{
    /// <summary>
    /// Hash of application + alert key + scope + source + rendered content. See AlertThrottle.BuildKey.
    /// </summary>
    public string ThrottleKey { get; set; }

    /// <summary>Application that raised the alert — kept for diagnostics, not for matching.</summary>
    public string Discriminator { get; set; }

    public string AlertKey { get; set; }

    /// <summary>When the last alert with this key was actually let through.</summary>
    public DateTime LastSentUtc { get; set; }

    /// <summary>How many repeats were swallowed since <see cref="LastSentUtc"/>.</summary>
    public int SuppressedCount { get; set; }

    /// <summary>First repeat swallowed in the current window. Null when nothing was suppressed.</summary>
    public DateTime? FirstSuppressedUtc { get; set; }
}
