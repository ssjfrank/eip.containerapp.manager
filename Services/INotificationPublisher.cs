using ContainerManager.Service.Models;

namespace ContainerManager.Service.Services;

public interface INotificationPublisher
{
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}