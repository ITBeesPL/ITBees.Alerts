using System.Security.Cryptography;
using System.Text;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Services;

/// <summary>
/// Cooldown for repeated alerts: the first occurrence of a given alert is let through, every
/// repeat of the same thing within the window is swallowed and only counted.
/// <para>
/// "The same thing" is alert key + scope + source + rendered content. Source is part of it so a
/// jammed cash point cannot silence the one next to it, and content is part of it so a *new*
/// fault on the same device still gets through immediately - only literal repetitions are
/// suppressed.
/// </para>
/// </summary>
internal static class AlertThrottle
{
    /// <summary>
    /// Shortest window wins when several rules match one event: a rule tightening its own
    /// cooldown must never silence somebody else's subscription. 0 (or no rule opting in)
    /// disables the cooldown, which is the default for every alert that sets nothing.
    /// </summary>
    public static int ResolveMinutes(IEnumerable<AlertRule> rules, AlertDefinition definition)
    {
        var effective = int.MaxValue;

        foreach (var rule in rules)
        {
            var minutes = rule.ThrottleMinutes ?? definition.ThrottleMinutes ?? 0;
            if (minutes <= 0)
                return 0;

            effective = Math.Min(effective, minutes);
        }

        return effective == int.MaxValue ? 0 : effective;
    }

    public static string BuildKey(AlertDefinition definition, AlertEvent alertEvent, string title, string message)
    {
        var canonical = string.Join('\n',
            alertEvent.Discriminator ?? string.Empty,
            definition.Key,
            alertEvent.Scope.Kind ?? string.Empty,
            alertEvent.Scope.Id?.ToString() ?? string.Empty,
            alertEvent.SourceId ?? string.Empty,
            title ?? string.Empty,
            message ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// True when the caller may record and deliver the alert - either nothing like it was seen
    /// before, or its window has expired. False means the repeat was swallowed and counted.
    /// </summary>
    public static bool TryEnterWindow(IServiceProvider sp, string key, AlertDefinition definition,
        AlertEvent alertEvent, int minutes, DateTime now, ILogger logger)
    {
        var window = TimeSpan.FromMinutes(minutes);
        var stateRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertThrottleState>>();
        var stateWoRepo = sp.GetRequiredService<IWriteOnlyRepository<AlertThrottleState>>();

        var state = stateRoRepo.GetData(x => x.ThrottleKey == key).FirstOrDefault();
        if (state == null)
        {
            try
            {
                stateWoRepo.InsertData(new AlertThrottleState
                {
                    ThrottleKey = key,
                    Discriminator = alertEvent.Discriminator,
                    AlertKey = definition.Key,
                    LastSentUtc = now,
                    SuppressedCount = 0
                });
                return true;
            }
            catch (Exception e)
            {
                // Another process inserted the same key a moment ago, so it is sending this
                // alert right now - stay quiet rather than duplicating it.
                logger.LogDebug(e, "Alert {Key} lost the cooldown insert race - treated as suppressed",
                    definition.Key);
                return false;
            }
        }

        if (now - state.LastSentUtc >= window)
        {
            stateWoRepo.UpdateData(x => x.ThrottleKey == key, x =>
            {
                x.LastSentUtc = now;
                x.SuppressedCount = 0;
                x.FirstSuppressedUtc = null;
            });
            return true;
        }

        stateWoRepo.UpdateData(x => x.ThrottleKey == key, x =>
        {
            x.SuppressedCount++;
            x.FirstSuppressedUtc ??= now;
        });

        return false;
    }
}
