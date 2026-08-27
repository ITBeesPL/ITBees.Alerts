using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Services;

/// <summary>
/// Sends every pending outbox row once. A failed delivery is recorded as failed and is not retried.
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
        var deliveryWoRepo = sp.GetRequiredService<IWriteOnlyRepository<AlertDelivery>>();
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
        foreach (var group in digestDeliveries.GroupBy(x => new { x.AlertContactGuid, x.Target }))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var deliveries = group.ToList();
            var result = await SendAsync(senders, AlertChannels.Sms, BuildDigestContext(deliveries),
                cancellationToken);
            Complete(deliveryWoRepo, deliveries.Select(x => x.Guid).ToList(), result);

            if (!result.Success)
                _logger.LogWarning("SMS digest for contact {ContactGuid} failed: {Error}",
                    group.Key.AlertContactGuid, result.Error);
        }

        foreach (var delivery in pending.Where(x => !digestDeliveryGuids.Contains(x.Guid)))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var result = await SendAsync(senders, delivery.Channel, BuildContext(delivery), cancellationToken);
            Complete(deliveryWoRepo, [delivery.Guid], result);

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

    private static void Complete(IWriteOnlyRepository<AlertDelivery> deliveryWoRepo, List<Guid> deliveryGuids,
        AlertDeliveryResult result)
    {
        var completedUtc = DateTime.UtcNow;
        deliveryWoRepo.UpdateData(x => deliveryGuids.Contains(x.Guid), x =>
        {
            x.Status = result.Success ? AlertDeliveryStatus.Sent : AlertDeliveryStatus.Failed;
            x.SentUtc = result.Success ? completedUtc : null;
            x.Error = result.Success ? null : Truncate(result.Error, 500);
        });
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
