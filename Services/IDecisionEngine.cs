using ContainerManager.Service.Models;

namespace ContainerManager.Service.Services;

public interface IDecisionEngine
{
    Task<Dictionary<string, ContainerAction>> DecideActionsAsync(List<QueueInfo> queues, CancellationToken cancellationToken = default);
    List<string> GetQueuesForContainer(string containerAppName);
    void ClearIdleStatesForQueues(List<string> queueNames);
}