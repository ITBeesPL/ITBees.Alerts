namespace ITBees.Alerts.Abstractions;

/// <summary>
/// Delivery channels an alert can travel through. Flags, because one rule usually fans out
/// to several at once. Every value here needs an <see cref="Interfaces.IAlertChannelSender"/>
/// registered in DI — channels ship as separate ITBees.Alerts.Channels.* packages, so an
/// application only pays for the ones it wires up.
/// </summary>
[Flags]
public enum AlertChannels
{
    None = 0,
    /// <summary>An in-application channel supplied by the host.</summary>
    InApp = 1,
    Email = 2,
    Sms = 4
}
