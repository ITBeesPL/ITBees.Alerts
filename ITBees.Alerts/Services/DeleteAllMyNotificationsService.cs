using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.Notifications.DbModels;

namespace ITBees.Alerts.Services;

public class DeleteAllMyNotificationsService : IDeleteAllMyNotificationsService
{
    private readonly IWriteOnlyRepository<DiscriminatedNotification> _discriminatedNotificationWoRepo;
    private readonly IAlertCurrentUserAccessor _alertCurrentUserAccessor;
    private readonly INotificationContextService _notificationContextService;

    public DeleteAllMyNotificationsService(
        IWriteOnlyRepository<DiscriminatedNotification> discriminatedNotificationWoRepo,
        IAlertCurrentUserAccessor alertCurrentUserAccessor,
        INotificationContextService notificationContextService)
    {
        _discriminatedNotificationWoRepo = discriminatedNotificationWoRepo;
        _alertCurrentUserAccessor = alertCurrentUserAccessor;
        _notificationContextService = notificationContextService;
    }

    public void Delete(string discriminator, string scopeKind, Guid? scopeId)
    {
        var context = _notificationContextService.Resolve(discriminator, scopeKind, scopeId);
        var userGuid = _alertCurrentUserAccessor.GetCurrentUserGuid();
        _discriminatedNotificationWoRepo.DeleteData(
            x => x.Discriminator == context.Discriminator &&
                 (context.ScopeKind == null || x.ScopeKind == context.ScopeKind) &&
                 (context.ScopeId == null || x.ScopeId == context.ScopeId) &&
                 x.UserAccountGuid == userGuid);
    }
}
