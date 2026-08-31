using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Interfaces;
using ITBees.Notifications.Controllers.Models;
using ITBees.Notifications.Interfaces;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Channels.InApp;

/// <summary>
/// Adapts an alert delivery to the public ITBees.Notifications API.
/// Notification persistence, context fields and inbox behavior stay inside ITBees.Notifications.
/// </summary>
public sealed class InAppAlertChannelSender : IAlertChannelSender
{
    private readonly IAlertInAppAudienceResolver _audienceResolver;
    private readonly INotificationToAllActiveUsersService _notificationsService;
    private readonly ILogger<InAppAlertChannelSender> _logger;

    public InAppAlertChannelSender(IAlertInAppAudienceResolver audienceResolver,
        INotificationToAllActiveUsersService notificationsService,
        ILogger<InAppAlertChannelSender> logger)
    {
        _audienceResolver = audienceResolver;
        _notificationsService = notificationsService;
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
            var recipients = userAccounts?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .Select(x => new RecipientUserAccountIm
                {
                    UserAccountGuid = x,
                    Email = string.Empty
                })
                .ToList() ?? [];

            if (recipients.Count == 0)
            {
                _logger.LogInformation("In-app alert for scope {Scope} has no audience", context.Scope);
                return AlertDeliveryResult.Ok();
            }

            var result = _notificationsService.SendToSelected(new NotificationToSelectedUsersIm
            {
                RecipientUserAccounts = recipients,
                Title = context.Subject,
                Message = context.Body,
                Link = context.Link,
                LinkOpenInNewWindow = false,
                Discriminator = context.Discriminator,
                ScopeKind = context.Scope.Kind,
                ScopeId = context.Scope.Id
            });

            return result.Success
                ? AlertDeliveryResult.Ok()
                : AlertDeliveryResult.Fail(result.Messaage);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "In-app alert delivery for scope {Scope} failed: {Message}",
                context.Scope, e.Message);
            return AlertDeliveryResult.Fail(e.Message);
        }
    }
}
