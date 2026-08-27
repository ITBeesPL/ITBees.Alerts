using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Turns a scope into the people who should see the bell light up. The host application
/// decides what "has access" means — employees of the parking's company, platform operators,
/// and so on. Returning an empty set simply means nobody is notified in-app.
/// </summary>
public interface IAlertInAppAudienceResolver
{
    Task<IReadOnlyCollection<Guid>> ResolveUserAccountsAsync(AlertScope scope, string discriminator,
        CancellationToken cancellationToken = default);
}
