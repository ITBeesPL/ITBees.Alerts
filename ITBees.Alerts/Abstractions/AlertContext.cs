using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Abstractions;

/// <summary>
/// Supplies the discriminator and scope policy of the host using the alert library. A host may
/// use a project name, role, tenant or any other stable value as its discriminator.
/// </summary>
public interface IAlertContext
{
    IReadOnlyCollection<string> AllowedScopeKinds { get; }
    bool RequiresConcreteNotificationScope { get; }
    string ResolveDiscriminator(string requestedDiscriminator = null);
    bool AllowsScope(string scopeKind);
}

public sealed class AlertContext : IAlertContext
{
    private readonly string _discriminator;
    private readonly HashSet<string> _allowedScopeKinds;

    public AlertContext(string discriminator, IEnumerable<string> allowedScopeKinds,
        bool requiresConcreteNotificationScope = false)
    {
        if (string.IsNullOrWhiteSpace(discriminator))
            throw new ArgumentException("Alert discriminator is required", nameof(discriminator));

        _discriminator = discriminator.Trim();
        RequiresConcreteNotificationScope = requiresConcreteNotificationScope;
        _allowedScopeKinds = (allowedScopeKinds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> AllowedScopeKinds => _allowedScopeKinds;
    public bool RequiresConcreteNotificationScope { get; }

    public string ResolveDiscriminator(string requestedDiscriminator = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedDiscriminator) &&
            !string.Equals(requestedDiscriminator.Trim(), _discriminator, StringComparison.OrdinalIgnoreCase))
            throw new FasApiErrorException("The requested discriminator is not available", 403);

        return _discriminator;
    }

    public bool AllowsScope(string scopeKind) =>
        !string.IsNullOrWhiteSpace(scopeKind) && _allowedScopeKinds.Contains(scopeKind);
}
