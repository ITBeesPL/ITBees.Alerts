using ITBees.Alerts.Configuration;
using ITBees.Alerts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Alerts.Channels.Email.Setup;

public static class EmailAlertChannelSetup
{
    /// <summary>
    /// Adds the e-mail channel using the sender and account factory supplied by the host.
    /// </summary>
    public static void Register(IServiceCollection services, AlertEmailChannelConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddScoped<IAlertChannelSender, EmailAlertChannelSender>();
    }
}
