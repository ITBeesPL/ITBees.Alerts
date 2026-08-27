using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.Notifications.DbModels;

namespace ITBees.Alerts.Services;

public class MyNotificationsService : IMyNotificationsService
{
    private const int DefaultPageSize = 3;
    private readonly IReadOnlyRepository<DiscriminatedNotification> _discriminatedNotificationRoRepo;
    private readonly IAlertCurrentUserAccessor _alertCurrentUserAccessor;
    private readonly INotificationContextService _notificationContextService;

    public MyNotificationsService(
        IReadOnlyRepository<DiscriminatedNotification> discriminatedNotificationRoRepo,
        IAlertCurrentUserAccessor alertCurrentUserAccessor,
        INotificationContextService notificationContextService)
    {
        _discriminatedNotificationRoRepo = discriminatedNotificationRoRepo;
        _alertCurrentUserAccessor = alertCurrentUserAccessor;
        _notificationContextService = notificationContextService;
    }

    public PaginatedResult<AlertMyNotificationVm> Get(string discriminator, string scopeKind, Guid? scopeId,
        bool onlyUnread, int? page, int? pageSize)
    {
        var context = _notificationContextService.Resolve(discriminator, scopeKind, scopeId);
        var userGuid = _alertCurrentUserAccessor.GetCurrentUserGuid();
        var effectivePage = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, 100);
        var query = Query(context, userGuid, onlyUnread);
        var count = query.Count();

        return new PaginatedResult<AlertMyNotificationVm>
        {
            AllElementsCount = count,
            CurrentPage = effectivePage,
            ElementsPerPage = effectivePageSize,
            Data = query.OrderByDescending(x => x.Received)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToList()
                .Select(x => new AlertMyNotificationVm(x))
                .ToList()
        };
    }

    private IQueryable<DiscriminatedNotification> Query(NotificationContext context, Guid userGuid,
        bool onlyUnread) => _discriminatedNotificationRoRepo.GetDataQueryable(
        x => x.Discriminator == context.Discriminator &&
             (context.ScopeKind == null || x.ScopeKind == context.ScopeKind) &&
             (context.ScopeId == null || x.ScopeId == context.ScopeId) &&
             x.UserAccountGuid == userGuid &&
             (!onlyUnread || !x.HasBeenRead));
}
