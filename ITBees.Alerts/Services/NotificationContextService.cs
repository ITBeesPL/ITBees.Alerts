using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Interfaces;
using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Services;

public class NotificationContextService : INotificationContextService
{
    private readonly IAlertContext _alertContext;
    private readonly IAlertScopeAuthorization _alertScopeAuthorization;

    public NotificationContextService(IAlertContext alertContext,
        IAlertScopeAuthorization alertScopeAuthorization)
    {
        _alertContext = alertContext;
        _alertScopeAuthorization = alertScopeAuthorization;
    }

    public NotificationContext Resolve(string discriminator, string scopeKind, Guid? scopeId)
    {
        var resolvedDiscriminator = _alertContext.ResolveDiscriminator(discriminator);

        if (string.IsNullOrWhiteSpace(scopeKind))
        {
            if (_alertContext.RequiresConcreteNotificationScope)
                throw new FasApiErrorException("This notification inbox requires a scope", 400);

            return new NotificationContext(resolvedDiscriminator, null, null);
        }

        var scope = new AlertScope(scopeKind, scopeId);
        if (!_alertContext.AllowsScope(scope.Kind))
            throw new FasApiErrorException($"Scope '{scope.Kind}' is not available", 403);
        if (_alertContext.RequiresConcreteNotificationScope && scope.Id == null)
            throw new FasApiErrorException("This notification inbox requires a concrete scope", 400);

        _alertScopeAuthorization.CheckRead(scope);
        return new NotificationContext(resolvedDiscriminator, scope.Kind, scope.Id);
    }
}
