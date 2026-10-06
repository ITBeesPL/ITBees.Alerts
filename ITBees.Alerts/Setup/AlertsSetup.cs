using ITBees.Alerts.Catalog;
using ITBees.Alerts.Channels.Email.Setup;
using ITBees.Alerts.Channels.InApp.Setup;
using ITBees.Alerts.Channels.Sms.Setup;
using ITBees.Alerts.Configuration;
using ITBees.Alerts.Interfaces;
using ITBees.Alerts.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITBees.Alerts.Setup;

/// <summary>
/// Wires the alerting core and the channels explicitly supplied by the host application.
/// <para>
/// The host still has to supply three adapters, because only it knows its own domain:
/// <see cref="IAlertScopeAuthorization"/>, <see cref="IAlertScopeNameResolver"/>,
/// <see cref="IAlertInAppAudienceResolver"/>, <see cref="IAlertContactBookResolver"/>
/// and <see cref="Abstractions.IAlertContext"/>,
/// plus at least one <see cref="IAlertCatalogSource"/>
/// with its alert kinds.
/// </para>
/// </summary>
public class AlertsSetup
{
    public void Register(IServiceCollection services, IConfigurationRoot configurationRoot)
    {
        Register(services);
    }

    public static void Register(IServiceCollection services)
    {
        // The catalog is built once from every registered source.
        services.AddSingleton<IAlertCatalog, AlertCatalog>();

        // Anti-spam defaults (burst digest, flapping). A host registers its own instance first
        // to change them.
        services.TryAddSingleton(AlertDeliveryOptions.Default);

        // Singleton so hosted services and SignalR handlers can inject it; it opens its own
        // scope per call.
        services.AddSingleton<IAlertPublisher, AlertPublisher>();

        services.AddScoped<IAlertDefinitionsService, AlertDefinitionsService>();
        services.AddScoped<IAlertRulesService, AlertRulesService>();
        services.AddScoped<IAlertContactsService, AlertContactsService>();
        services.AddScoped<IAlertHistoryService, AlertHistoryService>();
        services.AddScoped<IAlertTestService, AlertTestService>();
    }

    public static void Register(IServiceCollection services, AlertChannelsConfiguration channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Register(services);

        if (channels.Email != null)
            EmailAlertChannelSetup.Register(services, channels.Email);

        if (channels.Sms != null)
            SmsAlertChannelSetup.Register(services, channels.Sms);

        if (channels.InApp != null)
            InAppAlertChannelSetup.Register(services);
    }

    /// <summary>
    /// Starts the outbox dispatcher. Relational EF queries atomically claim rows across instances.
    /// Interrupted Processing rows require manual reconciliation; exactly-once delivery to an
    /// external provider is not guaranteed. Occurrence and outbox writes are separate operations.
    /// </summary>
    public static void AddDeliveryDispatcher(IServiceCollection services)
    {
        services.AddHostedService<AlertDeliveryDispatcher>();
    }

    /// <summary>
    /// Starts threshold evaluation for metric-backed alerts (server load, disk usage, device
    /// CPU). Needs at least one <see cref="IAlertMetricSource"/> registered.
    /// </summary>
    public static void AddMetricEvaluator(IServiceCollection services)
    {
        services.AddHostedService<AlertMetricEvaluator>();
    }

    /// <summary>Adds a module's alert kinds to the catalog.</summary>
    public static void AddCatalogSource<T>(IServiceCollection services) where T : class, IAlertCatalogSource
    {
        services.AddSingleton<IAlertCatalogSource, T>();
    }
}
