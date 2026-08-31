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
        var senders = sp.GetServices<IAlertChannelSender>().ToDictionary(x => x.Channel);
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
        foreach (var group in digestDeliveries.GroupBy(x => new { x.Discriminator, x.AlertContactGuid, x.Target }))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

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

    private static async Task<AlertDeliveryResult> SendAsync(
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
            return AlertDeliveryResult.Fail(e.Message);
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
