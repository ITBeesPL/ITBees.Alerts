using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Catalog;

/// <summary>
/// Aggregates every registered <see cref="IAlertCatalogSource"/> once at startup. Duplicate
/// keys are dropped with a warning instead of throwing — a misbehaving module must not stop
/// the host from booting.
/// </summary>
public class AlertCatalog : IAlertCatalog
{
    private readonly Dictionary<string, AlertDefinition> _byKey;

    public AlertCatalog(IEnumerable<IAlertCatalogSource> sources, ILogger<AlertCatalog> logger)
    {
        _byKey = new Dictionary<string, AlertDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in sources.SelectMany(s => s.GetDefinitions()))
        {
            if (string.IsNullOrWhiteSpace(definition?.Key))
            {
                logger.LogWarning("Alert catalog: definition without a key was skipped");
                continue;
            }

            if (!_byKey.TryAdd(definition.Key, definition))
                logger.LogWarning("Alert catalog: duplicate key {Key} was skipped", definition.Key);
        }

        All = _byKey.Values.OrderBy(x => x.Category).ThenBy(x => x.Key).ToList();
        logger.LogInformation("Alert catalog loaded with {Count} definitions", All.Count);
    }

    public IReadOnlyList<AlertDefinition> All { get; }

    public IReadOnlyList<AlertDefinition> ForScopeKind(string scopeKind) =>
        string.IsNullOrWhiteSpace(scopeKind)
            ? All
            : All.Where(x => string.Equals(x.ScopeKind, scopeKind, StringComparison.OrdinalIgnoreCase)).ToList();

    public AlertDefinition Get(string key) =>
        key != null && _byKey.TryGetValue(key, out var definition) ? definition : null;
}
