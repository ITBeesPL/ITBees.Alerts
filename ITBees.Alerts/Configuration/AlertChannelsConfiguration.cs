using ITBees.Mailing.Interfaces;
using ITBees.Models.EmailAccounts;
using ITBees.SerwerSmsIntegration;

namespace ITBees.Alerts.Configuration;

/// <summary>
/// Describes the channels supplied by the host application. The alert library never reads
/// SMTP or SMS credentials on its own; the host passes the same senders and account factories
/// that the rest of the application already uses.
/// </summary>
public sealed class AlertChannelsConfiguration
{
    public AlertEmailChannelConfiguration Email { get; init; }
    public AlertSmsChannelConfiguration Sms { get; init; }
    public AlertInAppChannelConfiguration InApp { get; init; }
}

public sealed class AlertEmailChannelConfiguration
{
    /// <summary>Resolves the e-mail sender registered by the host.</summary>
    public required Func<IServiceProvider, IEmailSendingService> SenderFactory { get; init; }

    /// <summary>
    /// Builds the sender account from the current host configuration. This is evaluated for
    /// every delivery, so configuration providers that support reload remain effective.
    /// </summary>
    public required Func<IServiceProvider, EmailAccount> AccountFactory { get; init; }
}

public sealed class AlertSmsChannelConfiguration
{
    /// <summary>Resolves the SMS gateway registered by the host.</summary>
    public required Func<IServiceProvider, ISerwerSmsIntegrationService> SenderFactory { get; init; }

    /// <summary>Resolves the provider sender name. Empty means an ECO message.</summary>
    public Func<IServiceProvider, string> SenderNameFactory { get; init; }

    /// <summary>Allows the host to redirect SMS messages to its logs in non-production environments.</summary>
    public Func<IServiceProvider, bool> OnlyToConsoleFactory { get; init; }
}

/// <summary>
/// Marker enabling the ITBees.Notifications channel. Its repositories and audience resolver
/// are domain services and therefore remain registered by the host.
/// </summary>
public sealed class AlertInAppChannelConfiguration;
