namespace ITBees.Alerts.Catalog;

public interface IAlertCatalog
{
    IReadOnlyList<AlertDefinition> All { get; }

    /// <summary>Definitions for one scope kind — "parking" for the operator screen, everything for admin.</summary>
    IReadOnlyList<AlertDefinition> ForScopeKind(string scopeKind);

    /// <summary>Null when the key is unknown; callers log and drop rather than throw.</summary>
    AlertDefinition Get(string key);
}
