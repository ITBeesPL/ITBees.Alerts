using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Services;

/// <summary>
/// Atomically claims pending rows before sending. Failed and interrupted attempts are not retried.
/// </summary>
public class AlertDeliveryDispatcher : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a row may sit in Processing before it is treated as abandoned by a crashed or
    /// redeployed dispatcher. It is only ever reported, never re-sent: the outcome of an
    /// interrupted send is unknown, so retrying could deliver the same alert twice.
    /// </summary>
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertDeliveryDispatcher> _logger;

    public AlertDeliveryDispatcher(IServiceProvider serviceProvider, ILogger<AlertDeliveryDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Alert delivery dispatcher cycle failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var senders = ResolveSenders(sp);
        var deliveryRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertDelivery>>();
        var contactRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertContact>>();
        var now = DateTime.UtcNow;

        var pending = deliveryRoRepo.GetDataQueryable(
                x => x.Status == AlertDeliveryStatus.Pending &&
                     (x.NotBeforeUtc == null || x.NotBeforeUtc <= now),
                x => x.AlertOccurrence)
            .OrderBy(x => x.CreatedUtc)
            .Take(BatchSize)
            .ToList();

        ReportStuckDeliveries(deliveryRoRepo, now);

        if (pending.Count == 0)
            return;

        var deferredContactGuids = pending
            .Where(x => x.Channel == AlertChannels.Sms && x.DeferredByQuietHours && x.AlertContactGuid != null)
            .Select(x => x.AlertContactGuid.Value)
            .Distinct()
            .ToList();
        var digestContactGuids = deferredContactGuids.Count == 0
            ? []
            : contactRoRepo.GetData(x => deferredContactGuids.Contains(x.Guid) && x.SmsDigestAfterQuietHours)
                .Select(x => x.Guid)
                .ToList();

        var digestDeliveries = digestContactGuids.Count == 0
            ? []
            : deliveryRoRepo.GetDataQueryable(
                    x => x.Status == AlertDeliveryStatus.Pending && x.Channel == AlertChannels.Sms &&
                         x.DeferredByQuietHours && x.AlertContactGuid != null &&
                         digestContactGuids.Contains(x.AlertContactGuid.Value) &&
                         (x.NotBeforeUtc == null || x.NotBeforeUtc <= now),
                    x => x.AlertOccurrence)
                .OrderBy(x => x.CreatedUtc)
                .ToList();

        var digestDeliveryGuids = digestDeliveries.Select(x => x.Guid).ToHashSet();
        var unroutable = new HashSet<AlertChannels>();
        foreach (var group in digestDeliveries.GroupBy(x => new { x.Discriminator, x.AlertContactGuid, x.Target }))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!senders.ContainsKey(AlertChannels.Sms))
            {
                if (unroutable.Add(AlertChannels.Sms))
                    _logger.LogWarning("No sender registered for channel {Channel}; digests stay pending",
                        AlertChannels.Sms);
                continue;
            }

            var claimGuid = Guid.NewGuid();
            var deliveries = await ClaimAsync(deliveryRoRepo, group.Select(x => x.Guid).ToList(),
                claimGuid, cancellationToken);
            if (deliveries.Count == 0)
                continue;
            var result = await SendAsync(senders, AlertChannels.Sms, BuildDigestContext(deliveries),
                cancellationToken);
            await CompleteAsync(deliveryRoRepo, claimGuid, result, cancellationToken);

            if (!result.Success)
                _logger.LogWarning("SMS digest for contact {ContactGuid} failed: {Error}",
                    group.Key.AlertContactGuid, result.Error);
        }

        foreach (var delivery in pending.Where(x => !digestDeliveryGuids.Contains(x.Guid)))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            // Leave the row Pending when its channel is not wired up. Failed is terminal, so
            // burning it here would make every alert raised before the channel was registered
            // unrecoverable - a missing sender is a configuration gap, not a delivery failure.
            if (!senders.ContainsKey(delivery.Channel))
            {
                if (unroutable.Add(delivery.Channel))
                    _logger.LogWarning(
                        "No sender registered for channel {Channel}; {Count} delivery(ies) stay pending",
                        delivery.Channel, pending.Count(x => x.Channel == delivery.Channel));
                continue;
            }

            var claimGuid = Guid.NewGuid();
            var claimed = await ClaimAsync(deliveryRoRepo, [delivery.Guid], claimGuid, cancellationToken);
            if (claimed.Count == 0)
                continue;
            var result = await SendAsync(senders, delivery.Channel, BuildContext(claimed[0]), cancellationToken);
            await CompleteAsync(deliveryRoRepo, claimGuid, result, cancellationToken);

            if (!result.Success)
                _logger.LogWarning("Alert delivery {Guid} on {Channel} failed: {Error}",
                    delivery.Guid, delivery.Channel, result.Error);
        }
    }

    /// <summary>
    /// Claimed rows whose dispatcher never came back are invisible to the outbox poll, which only
    /// looks at Pending. Surfacing the count here - and through the status filter on the delivery
    /// history endpoint - is what makes them reconcilable at all.
    /// </summary>
    private void ReportStuckDeliveries(IReadOnlyRepository<AlertDelivery> deliveryRoRepo, DateTime now)
    {
        var threshold = now - StuckAfter;
        var stuck = deliveryRoRepo.GetDataCount(x => x.Status == AlertDeliveryStatus.Processing &&
                                                     x.CreatedUtc <= threshold);
        if (stuck > 0)
            _logger.LogWarning(
                "{Count} alert delivery(ies) have been claimed as Processing for over {Minutes} minutes; " +
                "their send outcome is unknown and they are not retried - reconcile via the delivery log " +
                "filtered on Processing",
                stuck, StuckAfter.TotalMinutes);
    }

    /// <summary>
    /// One sender per channel. A host that wires a channel twice must not take the whole
    /// dispatcher down - ToDictionary would throw here, the cycle would be swallowed by the
    /// caller's catch, and every channel would stop delivering.
    /// </summary>
    private IReadOnlyDictionary<AlertChannels, IAlertChannelSender> ResolveSenders(IServiceProvider sp)
    {
        var senders = new Dictionary<AlertChannels, IAlertChannelSender>();
        foreach (var sender in sp.GetServices<IAlertChannelSender>())
            if (!senders.TryAdd(sender.Channel, sender))
                _logger.LogWarning("Duplicate alert channel sender registered for {Channel}; {Type} ignored",
                    sender.Channel, sender.GetType().Name);

        return senders;
    }

    private async Task<AlertDeliveryResult> SendAsync(
        IReadOnlyDictionary<AlertChannels, IAlertChannelSender> senders, AlertChannels channel,
        AlertDeliveryContext context, CancellationToken cancellationToken)
    {
        if (!senders.TryGetValue(channel, out var sender))
            return AlertDeliveryResult.Fail($"No sender registered for channel {channel}");

        try
        {
            return await sender.SendAsync(context, cancellationToken)
                   ?? AlertDeliveryResult.Fail("Channel returned no result");
        }
        catch (Exception e)
        {
            // The message is persisted and served to every scope reader, so it must not carry
            // provider internals (host names, ports, credential diagnostics).
            _logger.LogWarning(e, "Alert channel {Channel} threw while sending", channel);
            return AlertDeliveryResult.Fail($"Channel failed ({e.GetType().Name}); check application logs");
        }
    }

    private static async Task<List<AlertDelivery>> ClaimAsync(IReadOnlyRepository<AlertDelivery> deliveryRoRepo,
        List<Guid> deliveryGuids, Guid claimGuid, CancellationToken cancellationToken)
    {
        var claimed = await deliveryRoRepo.GetDataQueryable(x =>
                deliveryGuids.Contains(x.Guid) && x.Status == AlertDeliveryStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AlertDeliveryStatus.Processing)
                .SetProperty(x => x.ClaimGuid, (Guid?)claimGuid), cancellationToken);
        if (claimed == 0)
            return [];

        return await deliveryRoRepo.GetDataQueryable(x => x.ClaimGuid == claimGuid,
                x => x.AlertOccurrence)
            .AsNoTracking().OrderBy(x => x.CreatedUtc).ToListAsync(cancellationToken);
    }

    private static Task<int> CompleteAsync(IReadOnlyRepository<AlertDelivery> deliveryRoRepo, Guid claimGuid,
        AlertDeliveryResult result, CancellationToken cancellationToken)
    {
        var completedUtc = DateTime.UtcNow;
        var status = result.Success ? AlertDeliveryStatus.Sent : AlertDeliveryStatus.Failed;
        DateTime? sentUtc = result.Success ? completedUtc : null;
        var error = result.Success ? null : Truncate(result.Error, 500);
        return deliveryRoRepo.GetDataQueryable(x => x.ClaimGuid == claimGuid &&
                x.Status == AlertDeliveryStatus.Processing)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status)
                .SetProperty(x => x.SentUtc, sentUtc).SetProperty(x => x.Error, error), cancellationToken);
    }

    private static AlertDeliveryContext BuildContext(AlertDelivery delivery) => new()
    {
        AlertKey = delivery.AlertOccurrence?.AlertKey,
        Discriminator = delivery.Discriminator,
        Scope = new AlertScope(delivery.AlertOccurrence?.ScopeKind, delivery.AlertOccurrence?.ScopeId),
        Severity = delivery.Severity,
        Target = delivery.Target,
        Subject = delivery.Subject,
        Body = delivery.Body,
        Link = delivery.Link
    };

    private static AlertDeliveryContext BuildDigestContext(List<AlertDelivery> deliveries)
    {
        var first = deliveries[0];
        var entries = deliveries
            .GroupBy(x => new
            {
                AlertKey = x.AlertOccurrence?.AlertKey ?? x.Subject,
                ScopeKind = x.AlertOccurrence?.ScopeKind,
                ScopeId = x.AlertOccurrence?.ScopeId
            })
            .OrderBy(x => x.Min(delivery => delivery.CreatedUtc))
            .Select(x =>
            {
                var subject = x.Select(delivery => delivery.Subject)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? x.Key.AlertKey;
                return $"{x.Count()}× {subject}";
            })
            .ToList();
        var text = $"Podsumowanie ({deliveries.Count} alertów): {string.Join("; ", entries)}";

        return new AlertDeliveryContext
        {
            AlertKey = "sms.digest",
            Discriminator = first.Discriminator,
            Scope = new AlertScope(first.AlertOccurrence?.ScopeKind, first.AlertOccurrence?.ScopeId),
            Severity = deliveries.Max(x => x.Severity),
            Target = first.Target,
            Subject = text,
            Body = text,
            Link = null
        };
    }

    private static string Truncate(string text, int maxLength) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text.Substring(0, maxLength);
}
