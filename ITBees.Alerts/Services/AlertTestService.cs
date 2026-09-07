using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Services;

/// <summary>
/// Fires a real alert down the configured channels, marked as a test. Cheaper than waiting
/// for a genuine failure at three in the morning to discover the phone number had a typo.
/// </summary>
public class AlertTestService : IAlertTestService
{
    private readonly IAlertPublisher _publisher;
    private readonly IAlertCatalog _catalog;
    private readonly IAlertScopeAuthorization _authorization;
    private readonly IReadOnlyRepository<AlertDelivery> _deliveryRoRepo;
    private readonly IAlertContext _alertContext;

    public AlertTestService(IAlertPublisher publisher, IAlertCatalog catalog,
        IAlertScopeAuthorization authorization, IReadOnlyRepository<AlertDelivery> deliveryRoRepo,
        IAlertContext alertContext)
    {
        _publisher = publisher;
        _catalog = catalog;
        _authorization = authorization;
        _deliveryRoRepo = deliveryRoRepo;
        _alertContext = alertContext;
    }

    public AlertTestResultVm Send(AlertTestIm alertTestIm)
    {
        var scope = new AlertScope(alertTestIm.ScopeKind, alertTestIm.ScopeId);
        if (!_alertContext.AllowsScope(scope.Kind))
            throw new FasApiErrorException($"Scope '{scope.Kind}' is not available in this application", 403);
        _authorization.CheckWrite(scope);

        var definition = _catalog.Get(alertTestIm.AlertKey);
        if (definition == null)
            throw new FasApiErrorException($"Unknown alert key {alertTestIm.AlertKey}", 400);

        var before = DateTime.UtcNow;
        var discriminator = _alertContext.ResolveDiscriminator(alertTestIm.Discriminator);

        _publisher.RaiseAsync(new AlertEvent(definition.Key, scope)
            {
                Severity = definition.DefaultSeverity,
                Discriminator = discriminator,
                SourceName = "Test",
                IgnoreThrottle = true
            }
            .With("value", "—")
            .With("threshold", "—")
            .With("source", "Test")
            .With("device", "Test")).GetAwaiter().GetResult();

        var queued = _deliveryRoRepo.GetDataCount(x => x.CreatedUtc >= before &&
                                                   x.Discriminator == discriminator);

        return new AlertTestResultVm
        {
            Success = queued > 0,
            QueuedDeliveries = queued,
            Message = queued > 0
                ? "Test alert queued - it will be delivered within a minute."
                : "No rule matched this alert, so nothing was sent. Add a rule with at least one channel first."
        };
    }
}
