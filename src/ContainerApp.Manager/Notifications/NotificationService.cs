using System.Linq;
using Azure;
using Azure.Communication.Email;

namespace ContainerApp.Manager.Notifications;

public interface INotificationService
{
    Task SendAsync(IEnumerable<string> recipients, string subject, string htmlBody, CancellationToken cancellationToken);
}

public sealed class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly EmailClient _emailClient;

    public NotificationService(ILogger<NotificationService> logger, EmailClient emailClient)
    {
        _logger = logger;
        _emailClient = emailClient;
    }

    public async Task SendAsync(IEnumerable<string> recipients, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        try
        {
            var to = recipients.Select(r => new EmailAddress(r)).ToList();
            if (to.Count == 0)
            {
                _logger.LogDebug("No recipients for notification: {Subject}", subject);
                return;
            }
            var sender = Environment.GetEnvironmentVariable("ACS_EMAIL_SENDER") ?? "no-reply@example.com";
            var content = new EmailContent(subject) { Html = htmlBody };
            var message = new EmailMessage(sender, subject, content);
            foreach (var addr in to)
            {
                message.Recipients.To.Add(addr);
            }
            await _emailClient.SendAsync(global::Azure.WaitUntil.Completed, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification: {Subject}", subject);
        }
    }
}


