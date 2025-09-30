using ContainerManager.Service.Models;

namespace ContainerManager.Service.Services;

public interface INotificationPublisher
{
    Task PublishAsync(EmailMessage message, CancellationToken cancellationToken = default);
}