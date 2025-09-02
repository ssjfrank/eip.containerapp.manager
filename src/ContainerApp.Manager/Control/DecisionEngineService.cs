using ContainerApp.Manager.Azure;
using ContainerApp.Manager.Config;
using ContainerApp.Manager.Ems;
using ContainerApp.Manager.State;
using ContainerApp.Manager.Scheduling;
using Microsoft.Extensions.Options;

namespace ContainerApp.Manager.Control;

public sealed class DecisionEngineService : BackgroundService
{
    private readonly ILogger<DecisionEngineService> _logger;
    private readonly IEmsClient _emsClient;
    private readonly IContainerAppManager _acaManager;
    private readonly ActionExecutorService _actions;
    private readonly ILeaderElectionService _leader;
    private readonly MonitorOptions _options;
    private readonly IScheduleEvaluator _scheduleEvaluator;
    private readonly IStateStore _stateStore;

    public DecisionEngineService(
        ILogger<DecisionEngineService> logger,
        IEmsClient emsClient,
        IContainerAppManager acaManager,
        ActionExecutorService actions,
        ILeaderElectionService leader,
        IOptionsMonitor<MonitorOptions> options,
        IScheduleEvaluator scheduleEvaluator,
        IStateStore stateStore)
    {
        _logger = logger;
        _emsClient = emsClient;
        _acaManager = acaManager;
        _actions = actions;
        _leader = leader;
        _options = options.CurrentValue;
        _scheduleEvaluator = scheduleEvaluator;
        _stateStore = stateStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_leader.IsLeader)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            foreach (var mapping in _options.Mappings)
            {
                try
                {
                    var observations = new List<QueueObservation>(mapping.Queues.Count);
                    foreach (var q in mapping.Queues)
                    {
                        var obs = await _emsClient.ObserveQueueAsync(q, stoppingToken);
                        observations.Add(obs);
                    }

                    var anyDemand = observations.Any(o => o.ApproximateDepth >= mapping.StartThreshold);
                    var allIdle = observations.All(o => o.ApproximateDepth == 0);
                    var inSchedule = _scheduleEvaluator.IsInActiveWindow(mapping, DateTimeOffset.UtcNow, out var scheduledReplicas, out _);
                    var state = await _stateStore.LoadAsync(mapping.ContainerApp, stoppingToken);

                    // Update last non-zero time
                    if (observations.Any(o => o.ApproximateDepth > 0))
                    {
                        state.LastNonZeroDepthAt = DateTimeOffset.UtcNow;
                        await _stateStore.SaveAsync(mapping.ContainerApp, state, stoppingToken);
                    }

                    var status = await _acaManager.GetStatusAsync(mapping.ResourceGroup, mapping.ContainerApp, stoppingToken);

                    if ((anyDemand || inSchedule) && (status.MinReplicas ?? 0) < (inSchedule ? scheduledReplicas : mapping.DesiredReplicas))
                    {
                        var desired = inSchedule ? scheduledReplicas : mapping.DesiredReplicas;
                        await _actions.ExecuteAsync(mapping, ActionType.Start, desired, mapping.NotifyEmails, _options.CooldownMinutes, stoppingToken);
                    }
                    else if (!inSchedule && allIdle && (status.MinReplicas ?? 0) > 0)
                    {
                        var idleLongEnough = state.LastNonZeroDepthAt.HasValue
                            ? DateTimeOffset.UtcNow - state.LastNonZeroDepthAt.Value >= TimeSpan.FromMinutes(mapping.IdleTimeoutMinutes)
                            : true; // if we have no history, allow stop
                        if (idleLongEnough)
                        {
                            await _actions.ExecuteAsync(mapping, ActionType.Stop, 0, mapping.NotifyEmails, _options.CooldownMinutes, stoppingToken);
                        }
                    }

                    // No-listener anomaly: if any queue has depth > 0, but consumers are none/unknown for > threshold since last start
                    var anyDepth = observations.Any(o => o.ApproximateDepth > 0);
                    var consumersNoneOrUnknown = observations.All(o => o.ConsumerPresence != ConsumerPresenceStatus.Present);
                    if (anyDepth && consumersNoneOrUnknown && (status.MinReplicas ?? 0) > 0)
                    {
                        if (state.LastStart.HasValue && DateTimeOffset.UtcNow - state.LastStart.Value > TimeSpan.FromMinutes(mapping.NoListenerTimeoutMinutes))
                        {
                            await _actions.ExecuteAsync(mapping, ActionType.Restart, mapping.DesiredReplicas, mapping.NotifyEmails, _options.CooldownMinutes, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Decision loop error for app {App}", mapping.ContainerApp);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)), stoppingToken);
        }
    }
}


