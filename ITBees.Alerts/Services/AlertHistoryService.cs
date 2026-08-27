using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;

namespace ITBees.Alerts.Services;

public class AlertHistoryService : IAlertHistoryService
{
    private const int DefaultPageSize = 25;

    private readonly IReadOnlyRepository<AlertOccurrence> _occurrenceRoRepo;
    private readonly IReadOnlyRepository<AlertDelivery> _deliveryRoRepo;
    private readonly IAlertScopeAuthorization _authorization;
    private readonly IAlertContext _alertContext;

    public AlertHistoryService(
        IReadOnlyRepository<AlertOccurrence> occurrenceRoRepo,
        IReadOnlyRepository<AlertDelivery> deliveryRoRepo,
        IAlertScopeAuthorization authorization,
        IAlertContext alertContext)
    {
        _occurrenceRoRepo = occurrenceRoRepo;
        _deliveryRoRepo = deliveryRoRepo;
        _authorization = authorization;
        _alertContext = alertContext;
    }

    public PaginatedResult<AlertOccurrenceVm> GetOccurrences(string requestedDiscriminator, string scopeKind,
        Guid? scopeId,
        int? page, int? pageSize)
    {
        var scope = new AlertScope(scopeKind, scopeId);
        var discriminator = _alertContext.ResolveDiscriminator(requestedDiscriminator);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckRead(scope);

        var occurrenceGuids = _deliveryRoRepo
            .GetData(x => x.Discriminator == discriminator)
            .Select(x => x.AlertOccurrenceGuid)
            .Distinct()
            .ToList();

        return _occurrenceRoRepo
            .GetDataPaginated(
                x => occurrenceGuids.Contains(x.Guid) && x.ScopeKind == scope.Kind && x.ScopeId == scopeId,
                new SortOptions(page ?? 1, pageSize ?? DefaultPageSize, nameof(AlertOccurrence.CreatedUtc),
                    SortOrder.Descending))
            .MapTo(x => new AlertOccurrenceVm(x));
    }

    public PaginatedResult<AlertDeliveryVm> GetDeliveries(string requestedDiscriminator, string scopeKind,
        Guid? scopeId, int? page, int? pageSize)
    {
        var scope = new AlertScope(scopeKind, scopeId);
        var discriminator = _alertContext.ResolveDiscriminator(requestedDiscriminator);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckRead(scope);

        return _deliveryRoRepo
            .GetDataPaginated(
                x => x.Discriminator == discriminator &&
                     x.AlertOccurrence.ScopeKind == scope.Kind && x.AlertOccurrence.ScopeId == scopeId,
                new SortOptions(page ?? 1, pageSize ?? DefaultPageSize, nameof(AlertDelivery.CreatedUtc),
                    SortOrder.Descending),
                x => x.AlertOccurrence)
            .MapTo(x => new AlertDeliveryVm(x));
    }

    private void EnsureScopeAllowed(string scopeKind)
    {
        if (!_alertContext.AllowsScope(scopeKind))
            throw new ITBees.RestfulApiControllers.Exceptions.FasApiErrorException(
                $"Scope '{scopeKind}' is not available in this application", 403);
    }
}
