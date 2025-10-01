using System.Collections.Concurrent;
using ContainerManager.Service.Configuration;
using ContainerManager.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContainerManager.Service.Services;

public class DecisionEngine : IDecisionEngine
{
    private readonly ILogger<DecisionEngine> _logger;
    private readonly ManagerSettings _settings;
    private readonly ConcurrentDictionary<string, QueueIdleState> _idleStates = new();
    private readonly IReadOnlyDictionary<string, List<string>> _containerToQueuesMap;
    private readonly IReadOnlyDictionary<string, string> _queueToContainerMap;

    public DecisionEngine(
        ILogger<DecisionEngine> logger,
        IOptions<ManagerSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;

        // Build mappings
        _containerToQueuesMap = _settings.QueueContainerMappings;

        var queueToContainerTemp = new Dictionary<string, string>();
        foreach (var mapping in _settings.QueueContainerMappings)
        {
            foreach (var queueName in mapping.Value)
            {
                queueToContainerTemp[queueName] = mapping.Key;
            }
        }
        _queueToContainerMap = queueToContainerTemp;

        _logger.LogInformation("DecisionEngine initialized with {ContainerCount} container mappings", _containerToQueuesMap.Count);
    }

    public Task<Dictionary<string, ContainerAction>> DecideActionsAsync(List<QueueInfo> queues, CancellationToken cancellationToken = default)
    {
        var actions = new Dictionary<string, ContainerAction>();

        // Group queues by container app
        var containerQueues = new Dictionary<string, List<QueueInfo>>();

        foreach (var queue in queues)
        {
            if (_queueToContainerMap.TryGetValue(queue.Name, out var containerApp))
            {
                if (!containerQueues.ContainsKey(containerApp))
                {
                    containerQueues[containerApp] = new List<QueueInfo>();
                }
                containerQueues[containerApp].Add(queue);
            }
        }

        // Decide action for each container
        foreach (var (containerApp, containerQueueList) in containerQueues)
        {
            var action = DecideActionForContainer(containerApp, containerQueueList);
            actions[containerApp] = action;

            if (action != ContainerAction.None)
            {
                _logger.LogInformation("Decision for {ContainerApp}: {Action}", containerApp, action);
            }
        }

        return Task.FromResult(actions);
    }

    private ContainerAction DecideActionForContainer(string containerApp, List<QueueInfo> queues)
    {
        // Rule 1: Check if ANY queue has messages WITH receivers → Do nothing (working normally)
        // This rule runs FIRST to protect active message processing from interruption
        foreach (var queue in queues)
        {
            if (queue.PendingMessageCount > 0 && queue.ReceiverCount > 0)
            {
                _logger.LogDebug(
                    "Queue {QueueName} has {MessageCount} messages with {ReceiverCount} receivers → Container working normally",
                    queue.Name, queue.PendingMessageCount, queue.ReceiverCount);

                // Don't clear idle states here - let MonitoringWorker clear when operation actually starts
                return ContainerAction.None;
            }
        }

        // Rule 2: Check if ANY queue has messages without receivers → RESTART
        // Only runs if no queues are actively processing (Rule 1 didn't return)
        foreach (var queue in queues)
        {
            if (queue.PendingMessageCount > 0 && queue.ReceiverCount == 0)
            {
                _logger.LogInformation(
                    "Queue {QueueName} has {MessageCount} messages with no receivers → Restart {ContainerApp}",
                    queue.Name, queue.PendingMessageCount, containerApp);

                // Don't clear idle states here - let MonitoringWorker clear when operation actually starts
                return ContainerAction.Restart;
            }
        }

        // Rule 3: All queues are idle (no messages) → Check if should STOP
        bool allQueuesIdle = true;
        bool anyQueueHasReceivers = false;

        foreach (var queue in queues)
        {
            if (queue.PendingMessageCount > 0)
            {
                allQueuesIdle = false;
                break;
            }

            if (queue.ReceiverCount > 0)
            {
                anyQueueHasReceivers = true;

                // Track idle time using TryAdd for reliable new-entry detection
                var newIdleState = new QueueIdleState
                {
                    QueueName = queue.Name,
                    IdleStartTime = DateTime.UtcNow
                };

                bool wasAdded = _idleStates.TryAdd(queue.Name, newIdleState);
                var idleState = wasAdded ? newIdleState : _idleStates[queue.Name];

                // Only log if this is a newly added idle state
                if (wasAdded)
                {
                    _logger.LogDebug("Queue {QueueName} marked as idle at {Time}", queue.Name, idleState.IdleStartTime);
                }
            }
        }

        // If all queues are idle and at least one has receivers, check timeout
        if (allQueuesIdle && anyQueueHasReceivers)
        {
            var idleTimeout = TimeSpan.FromMinutes(_settings.IdleTimeoutMinutes);
            bool allIdleLongEnough = true;
            bool allQueuesHaveIdleState = true;

            foreach (var queue in queues)
            {
                if (queue.ReceiverCount > 0)
                {
                    // Check if this queue has an idle state tracked
                    if (!_idleStates.TryGetValue(queue.Name, out var idleState))
                    {
                        // Queue with receivers but no idle state yet - not ready to stop
                        allQueuesHaveIdleState = false;
                        _logger.LogDebug(
                            "Queue {QueueName} has receivers but idle state not yet tracked",
                            queue.Name);
                        break;
                    }

                    var idleDuration = idleState.IdleDuration ?? TimeSpan.Zero;

                    if (idleDuration < idleTimeout)
                    {
                        allIdleLongEnough = false;
                        _logger.LogDebug(
                            "Queue {QueueName} idle for {Duration}, waiting for {Timeout}",
                            queue.Name, idleDuration, idleTimeout);
                    }
                }
            }

            // Only stop if all queues have idle states AND all have been idle long enough
            if (allQueuesHaveIdleState && allIdleLongEnough)
            {
                _logger.LogInformation(
                    "All queues for {ContainerApp} idle for {IdleTimeout} → Stop container",
                    containerApp, idleTimeout);

                // Don't clear idle states here - let MonitoringWorker clear when operation actually starts
                return ContainerAction.Stop;
            }
        }

        return ContainerAction.None;
    }

    private void ClearIdleStates(List<QueueInfo> queues)
    {
        foreach (var queue in queues)
        {
            if (_idleStates.TryRemove(queue.Name, out _))
            {
                _logger.LogDebug("Cleared idle state for queue {QueueName}", queue.Name);
            }
        }
    }

    public List<string> GetQueuesForContainer(string containerAppName)
    {
        return _containerToQueuesMap.TryGetValue(containerAppName, out var queues)
            ? queues
            : new List<string>();
    }

    public void ClearIdleStatesForQueues(List<string> queueNames)
    {
        foreach (var queueName in queueNames)
        {
            if (_idleStates.TryRemove(queueName, out _))
            {
                _logger.LogDebug("Cleared idle state for queue {QueueName}", queueName);
            }
        }
    }
}