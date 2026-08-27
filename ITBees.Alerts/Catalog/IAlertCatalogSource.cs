namespace ITBees.Alerts.Catalog;

/// <summary>
/// Contributes alert definitions to the catalog. Register one per module; the aggregated
/// catalog is what the settings screen lists. This is the extension point that keeps
/// ITBees.Alerts free of any knowledge about parkings, invoices or anything else.
/// </summary>
public interface IAlertCatalogSource
{
    IEnumerable<AlertDefinition> GetDefinitions();
}
