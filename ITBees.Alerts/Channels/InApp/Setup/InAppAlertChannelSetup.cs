using ITBees.Alerts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Alerts.Channels.InApp.Setup;

public static class InAppAlertChannelSetup
{
    /// <summary>
    /// Adds the bell channel. The host must also register ITBees.Notifications itself
    /// (<c>NotificationsSetup</c> plus its controllers and <c>DbModelBuilder</c>) - that is what
    /// serves the bell to the frontend - and an <see cref="IAlertInAppAudienceResolver"/>
    /// saying who may see a given scope.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<IAlertChannelSender, InAppAlertChannelSender>();
    }
}
