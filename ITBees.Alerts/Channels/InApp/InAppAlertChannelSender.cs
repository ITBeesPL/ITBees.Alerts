using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.Notifications.DbModels;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Channels.InApp;

/// <summary>
/// The bell. Writes one <see cref="DiscriminatedNotification"/> per user who has access to the
/// scope. The discriminator and scope are persisted with the notification itself.
/// <para>
/// Contacts are irrelevant here: the audience is "whoever is logged in and allowed to see this
/// scope", resolved by the host through <see cref="IAlertInAppAudienceResolver"/>.
/// </para>
/// </summary>
public sealed class InAppAlertChannelSender : IAlertChannelSender
{
    private readonly IAlertInAppAudienceResolver _audienceResolver;
    private readonly IWriteOnlyRepository<DiscriminatedNotification> _notificationWoRepo;
    private readonly ILogger<InAppAlertChannelSender> _logger;

    public InAppAlertChannelSender(IAlertInAppAudienceResolver audienceResolver,
        IWriteOnlyRepository<DiscriminatedNotification> notificationWoRepo,
        ILogger<InAppAlertChannelSender> logger)
    {
        _audienceResolver = audienceResolver;
        _notificationWoRepo = notificationWoRepo;
        _logger = logger;
    }

    public AlertChannels Channel => AlertChannels.InApp;

    public async Task<AlertDeliveryResult> SendAsync(AlertDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userAccounts = await _audienceResolver.ResolveUserAccountsAsync(context.Scope,
                context.Discriminator, cancellationToken);
            if (userAccounts == null || userAccounts.Count == 0)
            {
                // Nobody has access to this scope - not a failure worth retrying.
                _logger.LogInformation("In-app alert for scope {Scope} has no audience", context.Scope);
                return AlertDeliveryResult.Ok();
            }

            var now = DateTime.UtcNow;
            var notifications = userAccounts
                .Distinct()
                .Where(x => x != Guid.Empty)
                .Select(userAccountGuid => new DiscriminatedNotification
                {
                    Guid = Guid.NewGuid(),
                    UserAccountGuid = userAccountGuid,
                    Received = now,
                    Title = context.Subject,
                    Message = context.Body,
                    Link = context.Link,
                    LinkOpenInNewWindow = false,
                    HasBeenRead = false,
                    HasBeenClicked = false,
                    Discriminator = context.Discriminator,
                    ScopeKind = context.Scope.Kind,
                    ScopeId = context.Scope.Id
                })
                .ToList();

            if (notifications.Count > 0)
                _notificationWoRepo.InsertData(notifications);

            return AlertDeliveryResult.Ok();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "In-app alert delivery for scope {Scope} failed: {Message}",
                context.Scope, e.Message);
            return AlertDeliveryResult.Fail(e.Message);
        }
    }
}
