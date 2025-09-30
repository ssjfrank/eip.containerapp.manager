using ContainerManager.Service.Models;

namespace ContainerManager.Service.Services;

public interface IEmsQueueMonitor
{
    bool IsConnected { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<List<QueueInfo>> GetAllQueuesAsync(CancellationToken cancellationToken = default);
    Task<int> GetReceiverCountAsync(string queueName, CancellationToken cancellationToken = default);
}