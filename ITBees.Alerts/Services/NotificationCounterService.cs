using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.Notifications.DbModels;

namespace ITBees.Alerts.Services;

public class NotificationCounterService : INotificationCounterService
{
    private readonly IReadOnlyRepository<DiscriminatedNotification> _discriminatedNotificationRoRepo;
    private readonly IAlertCurrentUserAccessor _alertCurrentUserAccessor;
    private readonly INotificationContextService _notificationContextService;

    public NotificationCounterService(
        IReadOnlyRepository<DiscriminatedNotification> discriminatedNotificationRoRepo,
        IAlertCurrentUserAccessor alertCurrentUserAccessor,
        INotificationContextService notificationContextService)
    {
        _discriminatedNotificationRoRepo = discriminatedNotificationRoRepo;
        _alertCurrentUserAccessor = alertCurrentUserAccessor;
        _notificationContextService = notificationContextService;
    }

    public AlertNotificationsCounterVm Get(string discriminator, string scopeKind, Guid? scopeId)
    {
        var context = _notificationContextService.Resolve(discriminator, scopeKind, scopeId);
        var userGuid = _alertCurrentUserAccessor.GetCurrentUserGuid();
        var query = _discriminatedNotificationRoRepo.GetDataQueryable(
            x => x.Discriminator == context.Discriminator &&
                 (context.ScopeKind == null || x.ScopeKind == context.ScopeKind) &&
                 (context.ScopeId == null || x.ScopeId == context.ScopeId) &&
                 x.UserAccountGuid == userGuid);

        return new AlertNotificationsCounterVm
        {
            TotalMessagesCount = query.Count(),
            UnreadMessagesCount = query.Count(x => !x.HasBeenRead)
        };
    }
}
