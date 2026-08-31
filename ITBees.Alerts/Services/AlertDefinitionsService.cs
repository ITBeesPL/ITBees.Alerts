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

    /// <summary>
    /// Without a scope kind this lists the whole catalog, which is the administration view -
    /// still narrowed to the kinds this application declares, so omitting the parameter cannot
    /// widen what a caller sees beyond its own scopes.
    /// </summary>
    public List<AlertDefinitionVm> GetAll(string scopeKind)
    {
        if (string.IsNullOrWhiteSpace(scopeKind))
            return _catalog.All
                .Where(x => _alertContext.AllowsScope(new AlertScope(x.ScopeKind).Kind))
                .Select(x => new AlertDefinitionVm(x))
                .ToList();

        var kind = new AlertScope(scopeKind).Kind;
        if (!_alertContext.AllowsScope(kind))
            throw new FasApiErrorException($"Scope '{scopeKind}' is not available in this application", 403);

        return _catalog.ForScopeKind(kind).Select(x => new AlertDefinitionVm(x)).ToList();
    }
}
