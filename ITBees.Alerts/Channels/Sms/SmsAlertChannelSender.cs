using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Configuration;
using ITBees.Alerts.Interfaces;
using ITBees.Alerts.Services;
using ITBees.SerwerSmsIntegration;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Channels.Sms;

/// <summary>
/// Sends alerts as SMS through SerwerSMS. Text is trimmed to one message part - SMS is the
/// channel someone reads while driving to the site, so it carries the headline and nothing
/// else; the detail is in the e-mail and in the panel.
/// </summary>
public sealed class SmsAlertChannelSender : IAlertChannelSender
{
    /// <summary>One GSM-7 part. Beyond this the provider bills a second message for no benefit.</summary>
    private const int MaxLength = 160;

    private readonly ISerwerSmsIntegrationService _smsService;
    private readonly Func<string> _senderNameFactory;
    private readonly Func<bool> _onlyToConsoleFactory;
    private readonly ILogger<SmsAlertChannelSender> _logger;

    public SmsAlertChannelSender(AlertSmsChannelConfiguration configuration,
        IServiceProvider serviceProvider, ILogger<SmsAlertChannelSender> logger)
    {
        _smsService = configuration.SenderFactory(serviceProvider);
        _senderNameFactory = configuration.SenderNameFactory == null
            ? () => string.Empty
            : () => configuration.SenderNameFactory(serviceProvider) ?? string.Empty;
        _onlyToConsoleFactory = configuration.OnlyToConsoleFactory == null
            ? () => false
            : () => configuration.OnlyToConsoleFactory(serviceProvider);
        _logger = logger;
    }

    public AlertChannels Channel => AlertChannels.Sms;

    public Task<AlertDeliveryResult> SendAsync(AlertDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        var phone = Normalize(context.Target);
        if (string.IsNullOrWhiteSpace(phone))
            return Task.FromResult(AlertDeliveryResult.Fail($"'{context.Target}' is not a phone number"));

        try
        {
            var message = BuildText(context);
            if (_onlyToConsoleFactory())
            {
                _logger.LogInformation(
                    "Alert SMS console mode. Phone: {Phone}, sender: {Sender}, message: {Message}",
                    phone,
                    _senderNameFactory(),
                    message);
                return Task.FromResult(AlertDeliveryResult.Ok());
            }

            _smsService.Send(phone, message, _senderNameFactory());
            return Task.FromResult(AlertDeliveryResult.Ok());
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Alert SMS to {Phone} failed: {Message}", phone, e.Message);
            return Task.FromResult(AlertDeliveryResult.Fail(e.Message));
        }
    }

    private static string BuildText(AlertDeliveryContext context)
    {
        return AlertTemplateRenderer.Shorten(context.Subject, MaxLength);
    }

    /// <summary>Strips spaces and dashes people type into contact forms; keeps a leading plus.</summary>
    private static string Normalize(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmed = phone.Trim();
        var hasPlus = trimmed.StartsWith("+");
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        if (digits.Length < 9)
            return null;

        return hasPlus ? "+" + digits : digits;
    }
}
