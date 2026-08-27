using System.Net;
using System.Text;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Configuration;
using ITBees.Alerts.Interfaces;
using ITBees.Mailing.Interfaces;
using ITBees.Models.EmailAccounts;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Channels.Email;

/// <summary>
/// Sends alerts as e-mail through the platform's default account. Formatting stays plain on
/// purpose - an alert is read on a phone at an awkward hour, so the subject carries the whole
/// message and the body carries the detail.
/// </summary>
public sealed class EmailAlertChannelSender : IAlertChannelSender
{
    private readonly IEmailSendingService _emailSendingService;
    private readonly Func<EmailAccount> _accountFactory;
    private readonly ILogger<EmailAlertChannelSender> _logger;

    public EmailAlertChannelSender(AlertEmailChannelConfiguration configuration,
        IServiceProvider serviceProvider, ILogger<EmailAlertChannelSender> logger)
    {
        _emailSendingService = configuration.SenderFactory(serviceProvider);
        _accountFactory = () => configuration.AccountFactory(serviceProvider);
        _logger = logger;
    }

    public AlertChannels Channel => AlertChannels.Email;

    public Task<AlertDeliveryResult> SendAsync(AlertDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Target) || !context.Target.Contains('@'))
            return Task.FromResult(AlertDeliveryResult.Fail($"'{context.Target}' is not an e-mail address"));

        try
        {
            var account = _accountFactory();
            var subject = BuildSubject(context);

            _emailSendingService.SendEmail(account, new[] { context.Target }, subject,
                BuildPlainBody(context), BuildHtmlBody(context));

            return Task.FromResult(AlertDeliveryResult.Ok());
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Alert e-mail to {Target} failed: {Message}", context.Target, e.Message);
            return Task.FromResult(AlertDeliveryResult.Fail(e.Message));
        }
    }

    private static string BuildSubject(AlertDeliveryContext context)
    {
        var prefix = SeverityPrefix(context.Severity);
        return $"{prefix} {context.Subject}";
    }

    private static string SeverityPrefix(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "[KRYTYCZNE]",
        AlertSeverity.Error => "[AWARIA]",
        AlertSeverity.Warning => "[UWAGA]",
        _ => "[INFO]"
    };

    private static string SeverityColor(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "#B4322A",
        AlertSeverity.Error => "#B4322A",
        AlertSeverity.Warning => "#A9660B",
        _ => "#2F45C5"
    };

    private static string BuildPlainBody(AlertDeliveryContext context)
    {
        var body = new StringBuilder();
        body.AppendLine(context.Subject);
        body.AppendLine();

        if (!string.IsNullOrWhiteSpace(context.Body))
        {
            body.AppendLine(context.Body);
            body.AppendLine();
        }

        body.AppendLine($"Czas: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        if (!string.IsNullOrWhiteSpace(context.Link))
            body.AppendLine($"Szczegóły: {context.Link}");

        return body.ToString();
    }

    private static string BuildHtmlBody(AlertDeliveryContext context)
    {
        var color = SeverityColor(context.Severity);
        var html = new StringBuilder();

        html.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#14171F\">");
        html.Append($"<div style=\"border-left:4px solid {color};padding:4px 0 4px 12px;margin-bottom:16px\">");
        html.Append($"<strong style=\"font-size:16px\">{WebUtility.HtmlEncode(context.Subject)}</strong></div>");

        if (!string.IsNullOrWhiteSpace(context.Body))
            html.Append($"<p>{WebUtility.HtmlEncode(context.Body).Replace("\n", "<br/>")}</p>");

        html.Append($"<p style=\"color:#6E7684\">Czas: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");

        if (!string.IsNullOrWhiteSpace(context.Link))
            html.Append($"<p><a href=\"{WebUtility.HtmlEncode(context.Link)}\">Zobacz szczegóły</a></p>");

        html.Append("</div>");
        return html.ToString();
    }
}
