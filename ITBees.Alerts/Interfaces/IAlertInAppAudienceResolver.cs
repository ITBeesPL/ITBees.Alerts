using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Resolves the users who should receive an in-application alert for a scope.
/// The host application owns this domain decision; the notification library remains generic.
/// </summary>
public interface IAlertInAppAudienceResolver
{
    Task<IReadOnlyCollection<Guid>> ResolveUserAccountsAsync(AlertScope scope, string discriminator,
        CancellationToken cancellationToken = default);
}
