using System.Collections.Concurrent;

namespace ITBees.Alerts.Services;

/// <summary>
/// Remembers alerts withdrawn inside their confirmation window, per alert + scope + source, so a
/// condition that keeps coming back for a few seconds is eventually reported anyway.
/// <para>
/// In memory on purpose: it only decides whether the next alert waits or not, and the worst a
/// restart can do is make an unstable link wait one more confirmation window.
/// </para>
/// </summary>
internal sealed class AlertFlappingTracker
{
    private readonly ConcurrentDictionary<string, State> _states = new();

    public static string BuildKey(string alertKey, string scopeKind, Guid? scopeId, string sourceId,
        string discriminator) =>
        $"{discriminator}|{alertKey}|{scopeKind}|{scopeId}|{sourceId}";

    public void RecordWithdrawal(string key, DateTime now)
    {
        var state = _states.GetOrAdd(key, _ => new State());
        lock (state)
            state.Withdrawals.Add(now);
    }

    /// <summary>
    /// True when <paramref name="count"/> withdrawals happened within <paramref name="window"/> and
    /// no flapping alert was let through in that window yet - the caller then delivers at once.
    /// One flapping alert per window: the next ones wait out their confirmation again.
    /// </summary>
    public bool TryEnterFlapping(string key, int count, TimeSpan window, DateTime now, out int withdrawals)
    {
        withdrawals = 0;
        if (count <= 0 || !_states.TryGetValue(key, out var state))
            return false;

        lock (state)
        {
            state.Withdrawals.RemoveAll(x => now - x > window);
            if (state.LastFlappingAlertUtc != null && now - state.LastFlappingAlertUtc.Value < window)
                return false;
            if (state.Withdrawals.Count < count)
                return false;

            withdrawals = state.Withdrawals.Count;
            state.Withdrawals.Clear();
            state.LastFlappingAlertUtc = now;
            return true;
        }
    }

    private sealed class State
    {
        public List<DateTime> Withdrawals { get; } = new();
        public DateTime? LastFlappingAlertUtc { get; set; }
    }
}
