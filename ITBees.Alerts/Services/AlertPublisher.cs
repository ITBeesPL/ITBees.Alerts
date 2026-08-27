using System.Text.Json;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
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
            var rules = MatchRules(sp, definition, alertEvent.Scope, severity, alertEvent.Discriminator);
            if (rules.Count == 0)
                return Task.CompletedTask;

            alertEvent.Values ??= new Dictionary<string, string>();
            var scopeName = sp.GetService<IAlertScopeNameResolver>()?.ResolveName(alertEvent.Scope);
            if (!string.IsNullOrWhiteSpace(scopeName) &&
                !alertEvent.Values.ContainsKey("parking"))
                alertEvent.Values["parking"] = scopeName;

            var title = AlertTemplateRenderer.Render(definition.DefaultTitleTemplate ?? alertEvent.Key,
                alertEvent.Values);
            var message = AlertTemplateRenderer.Render(definition.DefaultMessageTemplate, alertEvent.Values);
            title = IncludeScopeName(title, scopeName, null, " — ");
            message = IncludeScopeName(message, scopeName, "Parking: ", Environment.NewLine);
            var now = DateTime.UtcNow;

            var occurrence = sp.GetRequiredService<IWriteOnlyRepository<AlertOccurrence>>()
                .InsertData(new AlertOccurrence
                {
                    Guid = Guid.NewGuid(),
                    AlertKey = definition.Key,
                    ScopeKind = alertEvent.Scope.Kind,
                    ScopeId = alertEvent.Scope.Id,
                    Severity = severity,
                    Title = title,
                    Message = message,
                    SourceId = alertEvent.SourceId,
                    SourceName = alertEvent.SourceName,
                    Link = alertEvent.Link,
                    ValuesJson = Serialize(alertEvent.Values),
                    CreatedUtc = now
                });

            EnqueueDeliveries(sp, rules, occurrence, definition, alertEvent, scopeName, title, message,
                alertEvent.Link, severity, now);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to raise alert {Key}", alertEvent?.Key);
        }

        return Task.CompletedTask;
    }

    private static List<AlertRule> MatchRules(IServiceProvider sp, AlertDefinition definition, AlertScope scope,
        AlertSeverity severity, string discriminator)
    {
        var ruleRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertRule>>();
        var candidates = ruleRoRepo.GetDataQueryable(
                x => !x.Deleted && x.Enabled && x.AlertKey == definition.Key && x.ScopeKind == scope.Kind &&
                     (discriminator == null || x.Discriminator == discriminator))
            .ToList()
            .Where(x => severity >= x.MinSeverity)
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
            .Where(rule =>
            {
                var ruleTargets = targets.Where(x => x.AlertRuleGuid == rule.Guid).ToList();
                return ruleTargets.Count == 0 || ruleTargets.Any(x => x.ScopeId == scope.Id);
            })
            .ToList();
    }

    private static void EnqueueDeliveries(IServiceProvider sp, List<AlertRule> rules, AlertOccurrence occurrence,
        AlertDefinition definition, AlertEvent alertEvent, string scopeName, string title, string message,
        string link, AlertSeverity severity, DateTime now)
    {
        var deliveryWoRepo = sp.GetRequiredService<IWriteOnlyRepository<AlertDelivery>>();
        var recipientRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertRuleRecipient>>();
        var deliveries = new List<AlertDelivery>();
        var scopeDescriptor = $"{occurrence.ScopeKind}:{occurrence.ScopeId}";

        foreach (var applicationRules in rules
                     .Where(x => x.Channels.HasFlag(AlertChannels.InApp))
                     .GroupBy(x => x.Discriminator))
        {
            var inAppRule = applicationRules.First();
            var inAppTitle = RenderRuleTitle(inAppRule, definition, alertEvent, scopeName, title);
            deliveries.Add(NewDelivery(occurrence, inAppRule.Guid, null, inAppRule.Discriminator,
                AlertChannels.InApp, scopeDescriptor, inAppTitle, message, link, severity, now));
        }

        foreach (var rule in rules)
        {
            var ruleTitle = RenderRuleTitle(rule, definition, alertEvent, scopeName, title);
            var contactChannels = rule.Channels & ~AlertChannels.InApp;
            if (contactChannels == AlertChannels.None)
                continue;

            var recipients = recipientRoRepo
                .GetData(x => x.AlertRuleGuid == rule.Guid, x => x.AlertContact)
                .Where(x => x.AlertContact != null && x.AlertContact.Enabled && !x.AlertContact.Deleted)
                .ToList();

            foreach (var recipient in recipients)
            {
                var channels = contactChannels & recipient.Channels;
                if (channels.HasFlag(AlertChannels.Email) && !string.IsNullOrWhiteSpace(recipient.AlertContact.Email))
                    deliveries.Add(NewDelivery(occurrence, rule.Guid, recipient.AlertContact.Guid,
                        rule.Discriminator,
                        AlertChannels.Email,
                        recipient.AlertContact.Email, ruleTitle, message, link, severity, now));

                if (channels.HasFlag(AlertChannels.Sms) && !string.IsNullOrWhiteSpace(recipient.AlertContact.Phone))
                {
                    var notBeforeUtc = SmsQuietHoursSchedule.GetQuietHoursEndUtc(recipient.AlertContact, now);
                    deliveries.Add(NewDelivery(occurrence, rule.Guid, recipient.AlertContact.Guid,
                        rule.Discriminator, AlertChannels.Sms, recipient.AlertContact.Phone, ruleTitle, message,
                        link, severity, now,
                        notBeforeUtc));
                }
            }
        }

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

        if (uniqueDeliveries.Count > 0)
            deliveryWoRepo.InsertData(uniqueDeliveries);
    }

    private static AlertDelivery NewDelivery(AlertOccurrence occurrence, Guid? ruleGuid, Guid? contactGuid,
        string discriminator, AlertChannels channel, string target, string subject, string body, string link,
        AlertSeverity severity,
        DateTime now, DateTime? notBeforeUtc = null) => new()
    {
        Guid = Guid.NewGuid(),
        Discriminator = discriminator,
        AlertOccurrenceGuid = occurrence.Guid,
        AlertRuleGuid = ruleGuid,
        AlertContactGuid = contactGuid,
        Channel = channel,
        Target = target,
        Subject = subject,
        Body = body,
        Link = link,
        Severity = severity,
        Status = AlertDeliveryStatus.Pending,
        NotBeforeUtc = notBeforeUtc,
        DeferredByQuietHours = notBeforeUtc != null,
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

    private static string Serialize(IDictionary<string, string> values)
    {
        try
        {
            return values == null || values.Count == 0 ? null : JsonSerializer.Serialize(values);
        }
        catch
        {
            return null;
        }
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
