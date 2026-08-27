using ITBees.Alerts.Controllers.Models;
using ITBees.Interfaces.Repository;

namespace ITBees.Alerts.Interfaces;

public interface IMyNotificationsService
{
    PaginatedResult<AlertMyNotificationVm> Get(string discriminator, string scopeKind, Guid? scopeId,
        bool onlyUnread, int? page, int? pageSize);
}

public interface INotificationCounterService
{
    AlertNotificationsCounterVm Get(string discriminator, string scopeKind, Guid? scopeId);
}

public interface IDeleteAllMyNotificationsService
{
    void Delete(string discriminator, string scopeKind, Guid? scopeId);
}

public interface INotificationContextService
{
    NotificationContext Resolve(string discriminator, string scopeKind, Guid? scopeId);
}

public readonly record struct NotificationContext(string Discriminator, string ScopeKind, Guid? ScopeId);
