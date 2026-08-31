using ITBees.Alerts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Alerts.Channels.InApp.Setup;

public static class InAppAlertChannelSetup
{
    /// <summary>
    /// Registers Alerts as a producer for ITBees.Notifications. The host must register
    /// NotificationsSetup and IAlertInAppAudienceResolver.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<IAlertChannelSender, InAppAlertChannelSender>();
    }
}
