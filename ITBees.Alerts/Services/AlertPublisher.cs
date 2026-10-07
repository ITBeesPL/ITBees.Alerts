using System.Collections.Concurrent;
using System.Text.Json;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.Configuration;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Services;

/// <summary>
/// Matches one raised event to configured rules, records it and queues one delivery per target.
/// Registered as a singleton and opens its own DI scope per call.
/// </summary>
public class AlertPublisher : IAlertPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAlertCatalog _catalog;
    private readonly ILogger<AlertPublisher> _logger;
    private readonly AlertFlappingTracker _flappingTracker = new();

    /// <summary>Per occurrence and application: deliveries waiting out the confirmation window.</summary>
    private readonly ConcurrentDictionary<(Guid OccurrenceGuid, string Discriminator), HeldApplication>
        _heldApplications = new();

    public AlertPublisher(IServiceProvider serviceProvider, IAlertCatalog catalog, ILogger<AlertPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
        _logger = logger;
    }

    public Task RaiseAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            if (alertEvent == null || string.IsNullOrWhiteSpace(alertEvent.Key))
                return Task.CompletedTask;

            var definition = _catalog.Get(alertEvent.Key);
            if (definition == null)
            {
                _logger.LogWarning("Alert {Key} is not in the catalog - event dropped", alertEvent.Key);
                return Task.CompletedTask;
            }

            using var scope = _serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var severity = alertEvent.Severity ?? definition.DefaultSeverity;
            var rules = MatchRules(sp, definition, alertEvent.Scope, severity, alertEvent.Discriminator,
                alertEvent.RuleGuids);
            if (rules.Count == 0)
                return Task.CompletedTask;

            alertEvent.Values ??= new Dictionary<string, string>();
            NormalizePayload(alertEvent);
            var scopeName = sp.GetService<IAlertScopeNameResolver>()?.ResolveName(alertEvent.Scope);
            if (!string.IsNullOrWhiteSpace(scopeName) &&
                !alertEvent.Values.ContainsKey("parking"))
                alertEvent.Values["parking"] = scopeName;

            var title = AlertTemplateRenderer.Render(definition.DefaultTitleTemplate ?? alertEvent.Key,
                alertEvent.Values);
            var message = AlertTemplateRenderer.Render(definition.DefaultMessageTemplate, alertEvent.Values);
            title = IncludeScopeName(title, scopeName, null, " — ");
            message = IncludeScopeName(message, scopeName, "Parking: ", Environment.NewLine);
            var valuesJson = Serialize(definition.Key, alertEvent.Values);
            title = AlertTemplateRenderer.Shorten(title, AlertContentLimits.Title);
            message = AlertTemplateRenderer.Shorten(message, AlertContentLimits.Body);
            var now = DateTime.UtcNow;
            var options = sp.GetService<AlertDeliveryOptions>() ?? AlertDeliveryOptions.Default;

            // Confirmation, flapping and cooldown are decided per application (admin panel, operator
            // panel, ...). An event raised without a discriminator reaches the rules of several of
            // them, and ResolveAsync withdraws per application - so every one of them must keep its
            // own state here as well, or the two sides never meet (a flapping key built from the
            // event's null discriminator never matched a withdrawal recorded for "admin").
            var confirmation = alertEvent.SkipConfirmation
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Max(0, definition.ConfirmationSeconds ?? 0));
            var plans = rules
                .GroupBy(x => x.Discriminator)
                .Select(applicationRules => PlanApplication(sp, applicationRules.Key, applicationRules.ToList(),
                    definition, alertEvent, title, message, confirmation, options, now))
                .Where(x => x != null)
                .ToList();
            if (plans.Count == 0)
                return Task.CompletedTask;

            var occurrence = sp.GetRequiredService<IWriteOnlyRepository<AlertOccurrence>>()
                .InsertData(new AlertOccurrence
                {
                    Guid = Guid.NewGuid(),
                    AlertKey = definition.Key,
                    ScopeKind = alertEvent.Scope.Kind,
                    ScopeId = alertEvent.Scope.Id,
                    Severity = severity,
                    Title = title,
                    // The history shows the "unstable" note when any application reported it so.
                    Message = plans.FirstOrDefault(x => x.Flapping)?.Message ?? message,
                    SourceId = alertEvent.SourceId,
                    SourceName = alertEvent.SourceName,
                    Link = alertEvent.Link,
                    ValuesJson = valuesJson,
                    CreatedUtc = now
                });

            foreach (var plan in plans.Where(x => x.HoldUntilUtc != null))
                _heldApplications[(occurrence.Guid, plan.Discriminator)] =
                    new HeldApplication(plan.HoldUntilUtc.Value, plan.ThrottleKey);

            EnqueueDeliveries(sp, plans, occurrence, definition, alertEvent, scopeName, title, alertEvent.Link,
                severity, now, options);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to raise alert {Key}", alertEvent?.Key);
        }

        return Task.CompletedTask;
    }

    public Task ResolveAsync(string key, AlertScope scope, string sourceId, string discriminator,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(discriminator))
                return Task.CompletedTask;

            var definition = _catalog.Get(key);
            if (definition?.ConfirmationSeconds is not > 0)
                return Task.CompletedTask;

            var now = DateTime.UtcNow;
            PruneHeldApplications(now);
            var heldOccurrenceGuids = _heldApplications.Keys
                .Where(x => x.Discriminator == discriminator)
                .Select(x => x.OccurrenceGuid)
                .ToList();
            if (heldOccurrenceGuids.Count == 0)
                return Task.CompletedTask;

            var scopeKind = scope.Kind;
            var scopeId = scope.Id;

            using var serviceScope = _serviceProvider.CreateScope();
            var sp = serviceScope.ServiceProvider;
            var occurrenceRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertOccurrence>>();
            var occurrenceWoRepo = sp.GetRequiredService<IWriteOnlyRepository<AlertOccurrence>>();
            var deliveryRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertDelivery>>();
            var deliveryWoRepo = sp.GetRequiredService<IWriteOnlyRepository<AlertDelivery>>();

            var candidates = occurrenceRoRepo.GetData(x =>
                    heldOccurrenceGuids.Contains(x.Guid) && x.AlertKey == key &&
                    x.ScopeKind == scopeKind && x.ScopeId == scopeId && x.SourceId == sourceId)
                .ToList();

            foreach (var occurrence in candidates)
            {
                // Claiming the entry first makes a concurrent resolve of the same alert a no-op.
                if (!_heldApplications.TryRemove((occurrence.Guid, discriminator), out var held) ||
                    held.HoldUntilUtc <= now)
                    continue;

                // Nobody in this application has been told yet: remove only its deliveries and
                // release only its cooldown, so the next real alert is not swallowed as a "repeat"
                // of something nobody ever received. Held deliveries cannot have been claimed yet
                // (their NotBeforeUtc is still ahead) - the check is a safety net.
                if (deliveryRoRepo.GetDataCount(x => x.AlertOccurrenceGuid == occurrence.Guid &&
                                                     x.Discriminator == discriminator &&
                                                     x.Status != AlertDeliveryStatus.Pending) > 0)
                    continue;

                deliveryWoRepo.DeleteData(x => x.AlertOccurrenceGuid == occurrence.Guid &&
                                               x.Discriminator == discriminator &&
                                               x.Status == AlertDeliveryStatus.Pending);
                ReleaseThrottle(sp, held.ThrottleKey, occurrence);
                _flappingTracker.RecordWithdrawal(AlertFlappingTracker.BuildKey(definition.Key,
                    occurrence.ScopeKind, occurrence.ScopeId, occurrence.SourceId, discriminator), now);

                // The occurrence is shared by every application the event reached; it goes with
                // the last one that has nothing left in it.
                if (deliveryRoRepo.GetDataCount(x => x.AlertOccurrenceGuid == occurrence.Guid) == 0 &&
                    !_heldApplications.Keys.Any(x => x.OccurrenceGuid == occurrence.Guid))
                    occurrenceWoRepo.DeleteData(x => x.Guid == occurrence.Guid);

                _logger.LogInformation(
                    "Alert {Key} for {Source} withdrawn for {Discriminator} - the condition cleared after " +
                    "{Seconds:F0}s, inside its {Window}s confirmation window",
                    key, occurrence.SourceName ?? occurrence.SourceId, discriminator,
                    (now - occurrence.CreatedUtc).TotalSeconds, definition.ConfirmationSeconds);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to resolve alert {Key}", key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Confirmation, flapping and cooldown for one application. Null when the cooldown swallows the
    /// event for it. Flapping is decided first: the note it adds is part of the content the
    /// cooldown compares.
    /// </summary>
    private ApplicationPlan PlanApplication(IServiceProvider sp, string discriminator,
        List<AlertRule> applicationRules, AlertDefinition definition, AlertEvent alertEvent, string title,
        string message, TimeSpan confirmation, AlertDeliveryOptions options, DateTime now)
    {
        DateTime? holdUntilUtc = null;
        var flapping = false;
        if (confirmation > TimeSpan.Zero)
        {
            // A condition that keeps clearing and coming back is reported at once instead of
            // waiting out yet another window - see AlertFlappingTracker.
            var flappingWindow = definition.FlappingWindowMinutes is > 0
                ? TimeSpan.FromMinutes(definition.FlappingWindowMinutes.Value)
                : options.DefaultFlappingWindow;
            var flappingKey = AlertFlappingTracker.BuildKey(definition.Key, alertEvent.Scope.Kind,
                alertEvent.Scope.Id, alertEvent.SourceId, discriminator);
            if (_flappingTracker.TryEnterFlapping(flappingKey,
                    definition.FlappingCount ?? options.DefaultFlappingCount, flappingWindow, now,
                    out var withdrawals))
            {
                flapping = true;
                message = AlertTemplateRenderer.Shorten(
                    $"{message}\nStan niestabilny - krótkich wystąpień w ciągu ostatnich " +
                    $"{flappingWindow.TotalMinutes:0} min: {withdrawals}. Zgłoszone bez czekania na potwierdzenie.",
                    AlertContentLimits.Body);
            }
            else
            {
                holdUntilUtc = now + confirmation;
            }
        }

        // Cooldown, once the content is known: a repeat inside the window writes neither an
        // occurrence nor a delivery. Suppressing here rather than at delivery time is what keeps a
        // device stuck in a fault loop from filling the history table as well. Every application
        // keeps its own window, so a cooldown configured in one of them never silences or
        // unsilences the other.
        string throttleKey = null;
        if (!alertEvent.IgnoreThrottle)
        {
            var throttleMinutes = AlertThrottle.ResolveMinutes(applicationRules, definition);
            if (throttleMinutes > 0)
            {
                throttleKey = AlertThrottle.BuildKey(discriminator, definition, alertEvent, title, message);
                if (!AlertThrottle.TryEnterWindow(sp, throttleKey, discriminator, definition, throttleMinutes, now,
                        _logger))
                {
                    _logger.LogDebug(
                        "Alert {Key} suppressed for {Discriminator} - identical alert already sent within {Minutes} min",
                        definition.Key, discriminator, throttleMinutes);
                    return null;
                }
            }
        }

        return new ApplicationPlan(discriminator, applicationRules, message, holdUntilUtc, throttleKey, flapping);
    }

    /// <summary>
    /// Deletes the cooldown entry a withdrawn raise opened - by the key that raise computed, and only
    /// while the entry still belongs to it (written in the same second as the occurrence).
    /// </summary>
    private static void ReleaseThrottle(IServiceProvider sp, string throttleKey, AlertOccurrence occurrence)
    {
        if (throttleKey == null)
            return;

        var from = occurrence.CreatedUtc.AddSeconds(-1);
        var to = occurrence.CreatedUtc.AddSeconds(1);
        sp.GetRequiredService<IWriteOnlyRepository<AlertThrottleState>>()
            .DeleteData(x => x.ThrottleKey == throttleKey && x.LastSentUtc >= from && x.LastSentUtc <= to);
    }

    private void PruneHeldApplications(DateTime now)
    {
        foreach (var held in _heldApplications.Where(x => x.Value.HoldUntilUtc <= now).ToList())
            _heldApplications.TryRemove(held.Key, out _);
    }

    /// <summary>What one application gets out of a single raise.</summary>
    private sealed record ApplicationPlan(string Discriminator, List<AlertRule> Rules, string Message,
        DateTime? HoldUntilUtc, string ThrottleKey, bool Flapping);

    /// <summary>
    /// An application's share of an occurrence that is still waiting out its confirmation window,
    /// with the cooldown key its raise opened. Only this process knows it - a delivery's NotBeforeUtc
    /// may also come from quiet hours or a burst digest - so a blip that straddles a restart is
    /// delivered rather than withdrawn: the safe direction.
    /// </summary>
    private sealed record HeldApplication(DateTime HoldUntilUtc, string ThrottleKey);

    private static List<AlertRule> MatchRules(IServiceProvider sp, AlertDefinition definition, AlertScope scope,
        AlertSeverity severity, string discriminator, IReadOnlyCollection<Guid> ruleGuids)
    {
        var ruleRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertRule>>();
        var candidates = ruleRoRepo.GetDataQueryable(
                x => !x.Deleted && x.Enabled && x.AlertKey == definition.Key && x.ScopeKind == scope.Kind &&
                     x.MinSeverity <= severity &&
                     (discriminator == null || x.Discriminator == discriminator) &&
                     (ruleGuids == null || ruleGuids.Contains(x.Guid)))
            .ToList();
        if (candidates.Count == 0)
            return candidates;

        // A rule with no targets covers every object of its kind; a targeted one only fires for
        // the objects it lists.
        var guids = candidates.Select(x => x.Guid).ToList();
        var targets = sp.GetRequiredService<IReadOnlyRepository<AlertRuleTarget>>()
            .GetData(x => guids.Contains(x.AlertRuleGuid))
            .ToList();

        return candidates
            .Where(rule => AlertRuleCoverage.Covers(
                targets.Where(x => x.AlertRuleGuid == rule.Guid).ToList(), scope.Id))
            .ToList();
    }

    private static void EnqueueDeliveries(IServiceProvider sp, List<ApplicationPlan> plans, AlertOccurrence occurrence,
        AlertDefinition definition, AlertEvent alertEvent, string scopeName, string title, string link,
        AlertSeverity severity, DateTime now, AlertDeliveryOptions options)
    {
        var deliveries = new List<AlertDelivery>();
        foreach (var plan in plans)
            deliveries.AddRange(BuildApplicationDeliveries(sp, plan, occurrence, definition, alertEvent, scopeName,
                title, link, severity, now));

        var uniqueDeliveries = deliveries
            .GroupBy(x => new
            {
                x.Channel,
                x.Discriminator,
                Target = x.Target?.ToLowerInvariant(),
                ContactGuid = x.Channel == AlertChannels.Sms ? x.AlertContactGuid : null
            })
            .Select(x => x.First())
            .ToList();

        if (uniqueDeliveries.Count == 0)
            return;

        // A send somebody explicitly asked for (a test) goes out on its own, like it skips the cooldown.
        if (!alertEvent.IgnoreThrottle)
            JoinBurstDigests(sp, uniqueDeliveries, now, options.BurstDigestWindow);
        sp.GetRequiredService<IWriteOnlyRepository<AlertDelivery>>().InsertData(uniqueDeliveries);
    }

    /// <summary>
    /// The deliveries of one application, with its own message (an "unstable" note is added only
    /// where it decided so) and its own confirmation hold.
    /// </summary>
    private static List<AlertDelivery> BuildApplicationDeliveries(IServiceProvider sp, ApplicationPlan plan,
        AlertOccurrence occurrence, AlertDefinition definition, AlertEvent alertEvent, string scopeName,
        string title, string link, AlertSeverity severity, DateTime now)
    {
        var recipientRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertRuleRecipient>>();
        var deliveries = new List<AlertDelivery>();
        var message = plan.Message;
        var holdUntilUtc = plan.HoldUntilUtc;

        var inAppRule = plan.Rules.FirstOrDefault(x => x.Channels.HasFlag(AlertChannels.InApp));
        if (inAppRule != null)
        {
            var inAppTitle = RenderRuleTitle(inAppRule, definition, alertEvent, scopeName, title);
            deliveries.Add(NewDelivery(occurrence, inAppRule.Guid, null, inAppRule.Discriminator,
                AlertChannels.InApp, $"{occurrence.ScopeKind}:{occurrence.ScopeId}", inAppTitle, message, link,
                severity, now, holdUntilUtc));
        }

        foreach (var rule in plan.Rules)
        {
            var ruleTitle = RenderRuleTitle(rule, definition, alertEvent, scopeName, title);
            var contactChannels = rule.Channels & ~AlertChannels.InApp;
            if (contactChannels == AlertChannels.None)
                continue;

            var recipients = recipientRoRepo
                .GetData(x => x.AlertRuleGuid == rule.Guid, x => x.AlertContact)
                .Where(x => x.AlertContact != null && x.AlertContact.Enabled && !x.AlertContact.Deleted &&
                            x.AlertContact.Discriminator == rule.Discriminator)
                .ToList();

            foreach (var recipient in recipients)
            {
                var channels = contactChannels & recipient.Channels;
                if (channels.HasFlag(AlertChannels.Email) && !string.IsNullOrWhiteSpace(recipient.AlertContact.Email))
                    deliveries.Add(NewDelivery(occurrence, rule.Guid, recipient.AlertContact.Guid,
                        rule.Discriminator,
                        AlertChannels.Email,
                        recipient.AlertContact.Email, ruleTitle, message, link, severity, now, holdUntilUtc));

                if (channels.HasFlag(AlertChannels.Sms) && !string.IsNullOrWhiteSpace(recipient.AlertContact.Phone))
                {
                    var quietHoursEndUtc = SmsQuietHoursSchedule.GetQuietHoursEndUtc(recipient.AlertContact, now);
                    deliveries.Add(NewDelivery(occurrence, rule.Guid, recipient.AlertContact.Guid,
                        rule.Discriminator, AlertChannels.Sms, recipient.AlertContact.Phone, ruleTitle, message,
                        link, severity, now, Later(holdUntilUtc, quietHoursEndUtc), quietHoursEndUtc != null));
                }
            }
        }

        return deliveries;
    }

    /// <summary>
    /// Burst digest: when this address already got (or is about to get) an alert within the
    /// window, the new one waits for the end of that window and the dispatcher sends everything
    /// due for the address as one digest. The first alert of a quiet period is never delayed.
    /// SMS held by quiet hours keep their own digest-after-quiet-hours path.
    /// </summary>
    private static void JoinBurstDigests(IServiceProvider sp, List<AlertDelivery> deliveries, DateTime now,
        TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            return;

        var deliveryRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertDelivery>>();
        var since = now - window;
        foreach (var delivery in deliveries.Where(x =>
                     x.Channel is AlertChannels.Email or AlertChannels.Sms && !x.DeferredByQuietHours))
        {
            var recent = deliveryRoRepo.GetDataQueryable(x =>
                    x.Channel == delivery.Channel && x.Discriminator == delivery.Discriminator &&
                    x.Target == delivery.Target && !x.DeferredByQuietHours && x.CreatedUtc >= since &&
                    x.Status != AlertDeliveryStatus.Failed)
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => new { x.CreatedUtc, x.NotBeforeUtc })
                .FirstOrDefault();
            if (recent == null)
                continue;

            var digestAtUtc = recent.NotBeforeUtc > now ? recent.NotBeforeUtc.Value : recent.CreatedUtc + window;
            if (digestAtUtc > now)
                delivery.NotBeforeUtc = Later(delivery.NotBeforeUtc, digestAtUtc);
        }
    }

    private static DateTime? Later(DateTime? first, DateTime? second) =>
        first == null ? second : second == null ? first : first > second ? first : second;

    private static AlertDelivery NewDelivery(AlertOccurrence occurrence, Guid? ruleGuid, Guid? contactGuid,
        string discriminator, AlertChannels channel, string target, string subject, string body, string link,
        AlertSeverity severity,
        DateTime now, DateTime? notBeforeUtc = null, bool deferredByQuietHours = false) => new()
    {
        Guid = Guid.NewGuid(),
        Discriminator = discriminator,
        AlertOccurrenceGuid = occurrence.Guid,
        AlertRuleGuid = ruleGuid,
        AlertContactGuid = contactGuid,
        Channel = channel,
        Target = target,
        Subject = AlertTemplateRenderer.Shorten(subject, AlertContentLimits.Title),
        Body = body,
        Link = link,
        Severity = severity,
        Status = AlertDeliveryStatus.Pending,
        NotBeforeUtc = notBeforeUtc,
        DeferredByQuietHours = deferredByQuietHours,
        CreatedUtc = now
    };

    private static string RenderRuleTitle(AlertRule rule, AlertDefinition definition, AlertEvent alertEvent,
        string scopeName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rule.CustomMessage))
            return fallback;

        var title = AlertTemplateRenderer.Render(rule.CustomMessage ?? definition.DefaultTitleTemplate ??
            alertEvent.Key, alertEvent.Values);
        return IncludeScopeName(title, scopeName, null, " — ");
    }

    /// <summary>
    /// Sheds the largest entries until the serialized form fits, rather than throwing away the
    /// whole alert. Values are display detail; the title, message and routing survive regardless.
    /// </summary>
    private string Serialize(string alertKey, IDictionary<string, string> values)
    {
        if (values == null || values.Count == 0)
            return null;

        var remaining = new Dictionary<string, string>(values);
        var json = JsonSerializer.Serialize(remaining);
        while (json.Length > AlertContentLimits.ValuesJson && remaining.Count > 0)
        {
            var largest = remaining.OrderByDescending(x => x.Value?.Length ?? 0).First().Key;
            remaining.Remove(largest);
            _logger.LogWarning("Alert {Key}: value '{Name}' dropped to fit the serialized limit",
                alertKey, largest);
            json = JsonSerializer.Serialize(remaining);
        }

        return remaining.Count == 0 ? null : json;
    }

    private void NormalizePayload(AlertEvent alertEvent)
    {
        var dropped = AlertPayloadNormalizer.Normalize(alertEvent);
        if (dropped > 0)
            _logger.LogWarning("Alert {Key}: {Count} value(s) dropped as unusable or over the entry limit",
                alertEvent.Key, dropped);
    }

    private static string IncludeScopeName(string text, string scopeName, string prefix, string separator)
    {
        if (string.IsNullOrWhiteSpace(scopeName) ||
            (!string.IsNullOrWhiteSpace(text) && text.Contains(scopeName, StringComparison.OrdinalIgnoreCase)))
            return text;

        var scopeText = $"{prefix}{scopeName}";
        return string.IsNullOrWhiteSpace(text) ? scopeText : $"{text}{separator}{scopeText}";
    }
}
