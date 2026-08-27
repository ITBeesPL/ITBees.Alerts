using ITBees.Alerts.Catalog;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.Interfaces;
using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Services;

public class AlertDefinitionsService : IAlertDefinitionsService
{
    private readonly IAlertCatalog _catalog;
    private readonly IAlertContext _alertContext;

    public AlertDefinitionsService(IAlertCatalog catalog, IAlertContext alertContext)
    {
        _catalog = catalog;
        _alertContext = alertContext;
    }

    public List<AlertDefinitionVm> GetAll(string scopeKind)
    {
        if (!_alertContext.AllowsScope(scopeKind))
            throw new FasApiErrorException($"Scope '{scopeKind}' is not available in this application", 403);

        return _catalog.ForScopeKind(scopeKind).Select(x => new AlertDefinitionVm(x)).ToList();
    }
}
