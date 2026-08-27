using ITBees.Alerts.Configuration;
using ITBees.Alerts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Alerts.Channels.Sms.Setup;

public static class SmsAlertChannelSetup
{
    /// <summary>
    /// Adds the SMS channel using the gateway and behaviour supplied by the host.
    /// </summary>
    public static void Register(IServiceCollection services, AlertSmsChannelConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddScoped<IAlertChannelSender, SmsAlertChannelSender>();
    }
}
