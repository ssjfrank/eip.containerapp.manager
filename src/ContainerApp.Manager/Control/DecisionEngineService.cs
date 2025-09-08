using ContainerApp.Manager.Azure;
using ContainerApp.Manager.Config;
using ContainerApp.Manager.Ems;
using ContainerApp.Manager.State;
using ContainerApp.Manager.Scheduling;
using Microsoft.Extensions.Options;

namespace ContainerApp.Manager.Control;

public enum ScalingAction
{
    None,
    Start,
    Stop,
    Restart
}

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
                    // Observe all queues for this container app
                    foreach (var q in mapping.Queues)
                    {
                        var obs = await _emsClient.ObserveQueueAsync(q, stoppingToken);
                        observations.Add(obs);
                    }

                    // Load current state
                    var state = await _stateStore.LoadAsync(mapping.ContainerApp, stoppingToken);
                    var status = await _acaManager.GetStatusAsync(mapping.ResourceGroup, mapping.ContainerApp, stoppingToken);
                    var now = DateTimeOffset.UtcNow;
                    
                    // Update queue consumer status tracking
                    foreach (var obs in observations)
                    {
                        if (!state.QueueConsumerStatus.ContainsKey(obs.QueueName))
                            state.QueueConsumerStatus[obs.QueueName] = new QueueConsumerState();
                            
                        var queueState = state.QueueConsumerStatus[obs.QueueName];
                        var previousMessageCount = queueState.MessageCount;
                        queueState.MessageCount = obs.ApproximateDepth;
                        queueState.HasActiveConsumers = obs.ConsumerPresence == ConsumerPresenceStatus.Present;
                        
                        if (obs.HasMessages)
                        {
                            queueState.LastMessageSeen = now;
                            state.LastNonZeroDepthAt = now;
                            
                            // Track when messages first appeared for processing time alerts
                            if (previousMessageCount == 0 || !queueState.FirstMessageSeenAt.HasValue)
                            {
                                queueState.FirstMessageSeenAt = now;
                                queueState.LastProcessingAlert = null;
                                queueState.ProcessingAlertCount = 0;
                                _logger.LogDebug("First messages detected in queue {Queue}, starting processing timer", obs.QueueName);
                            }
                        }
                        else
                        {
                            // No messages - reset processing duration tracking
                            if (queueState.FirstMessageSeenAt.HasValue)
                            {
                                _logger.LogDebug("Queue {Queue} is now empty, resetting processing timer", obs.QueueName);
                                queueState.FirstMessageSeenAt = null;
                                queueState.LastProcessingAlert = null;
                                queueState.ProcessingAlertCount = 0;
                            }
                        }
                        
                        if (queueState.HasActiveConsumers)
                        {
                            queueState.LastConsumerSeen = now;
                        }
                    }

                    // Check schedule window status
                    var inSchedule = _scheduleEvaluator.IsInActiveWindow(mapping, now, out var scheduledReplicas, out var scheduleWindow);
                    
                    // Update schedule tracking
                    if (inSchedule && (!state.ScheduleActiveUntil.HasValue || state.ScheduleActiveUntil < now))
                    {
                        state.LastScheduleStart = now;
                        state.ScheduleActiveUntil = now.AddMinutes(scheduleWindow?.DurationMinutes ?? 60);
                        _logger.LogInformation("Container app {App} entered scheduled window until {Until}", 
                            mapping.ContainerApp, state.ScheduleActiveUntil);
                    }

                    // Simplified scaling logic
                    var hasMessages = observations.Any(obs => obs.HasMessages);
                    var isRunning = (status.MinReplicas ?? 0) > 0;
                    var shouldScale = await DetermineScalingAction(mapping, observations, state, inSchedule, scheduledReplicas, isRunning, now);
                    
                    if (shouldScale != ScalingAction.None)
                    {
                        await ExecuteScalingAction(mapping, shouldScale, scheduledReplicas, state, stoppingToken);
                    }
                    
                    // Check for long message processing and send alerts
                    if (_options.MessageProcessingAlerts.Enabled)
                    {
                        await CheckLongProcessingMessages(mapping, state, now, stoppingToken);
                    }
                    
                    // Always save updated state
                    await _stateStore.SaveAsync(mapping.ContainerApp, state, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Decision loop error for app {App}", mapping.ContainerApp);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task<ScalingAction> DetermineScalingAction(AppMapping mapping, List<QueueObservation> observations, 
        RuntimeState state, bool inSchedule, int scheduledReplicas, bool isRunning, DateTimeOffset now)
    {
        var hasMessages = observations.Any(obs => obs.HasMessages);
        
        // Priority 1: Schedule window protection - never stop during scheduled periods
        if (inSchedule)
        {
            if (!isRunning)
            {
                _logger.LogInformation("Starting {App} for scheduled window with {Replicas} replicas", 
                    mapping.ContainerApp, scheduledReplicas);
                return ScalingAction.Start;
            }
            // App is running and in schedule - don't stop regardless of queue state
            return ScalingAction.None;
        }
        
        // Priority 2: Simple message-based scaling (outside schedule windows)
        if (hasMessages && !isRunning)
        {
            _logger.LogInformation("Starting {App} due to messages in queues: {Queues}", 
                mapping.ContainerApp, string.Join(", ", observations.Where(o => o.HasMessages).Select(o => o.QueueName)));
            return ScalingAction.Start;
        }
        
        if (!hasMessages && isRunning)
        {
            _logger.LogInformation("Stopping {App} due to no messages in any queue", mapping.ContainerApp);
            return ScalingAction.Stop;
        }
        
        // Priority 3: Simple restart logic - only if no consumers detected
        if (hasMessages && isRunning)
        {
            return await EvaluateRestartNeed(mapping, observations, state, now);
        }
        
        return ScalingAction.None;
    }

    private async Task<ScalingAction> EvaluateRestartNeed(AppMapping mapping, List<QueueObservation> observations, 
        RuntimeState state, DateTimeOffset now)
    {
        // Check if we've exceeded max restart attempts
        if (state.RestartAttemptCount >= mapping.MaxRestartAttempts)
        {
            _logger.LogWarning("Container app {App} has reached maximum restart attempts ({Max})", 
                mapping.ContainerApp, mapping.MaxRestartAttempts);
            return ScalingAction.None;
        }
        
        // Check if we're in restart cooldown
        if (state.LastRestartTime.HasValue && 
            now - state.LastRestartTime.Value < TimeSpan.FromMinutes(mapping.RestartCooldownMinutes))
        {
            return ScalingAction.None;
        }
        
        // SIMPLIFIED LOGIC: Only restart if NO consumers detected (not if consumers are slow)
        var messagesWithoutConsumers = observations.Where(obs => 
            obs.HasMessages && obs.ConsumerPresence != ConsumerPresenceStatus.Present).ToList();
            
        if (messagesWithoutConsumers.Any())
        {
            // Allow startup grace period after last start (time for consumers to register)
            var gracePeriodExpired = !state.LastStart.HasValue || 
                now - state.LastStart.Value > TimeSpan.FromMinutes(mapping.StartupGracePeriodMinutes);
                
            if (gracePeriodExpired)
            {
                // Multi-queue protection: Don't restart if ANY other queue has active consumers
                var otherActiveQueues = observations.Where(obs => 
                    obs.ConsumerPresence == ConsumerPresenceStatus.Present && 
                    !messagesWithoutConsumers.Any(mwc => mwc.QueueName == obs.QueueName)).ToList();
                    
                if (otherActiveQueues.Any())
                {
                    _logger.LogWarning("Skipping restart of {App} - other queues have active consumers: {ActiveQueues}", 
                        mapping.ContainerApp, string.Join(", ", otherActiveQueues.Select(q => q.QueueName)));
                    
                    // Send notification about the conflict
                    await _actions.ExecuteAsync(mapping, ActionType.None, 0, mapping.NotifyEmails, 0, CancellationToken.None);
                    return ScalingAction.None;
                }
                
                _logger.LogWarning("Container app {App} restart needed - messages without consumers in queues: {Queues}", 
                    mapping.ContainerApp, string.Join(", ", messagesWithoutConsumers.Select(q => q.QueueName)));
                return ScalingAction.Restart;
            }
        }
        
        // If consumers are present, let them work (no restart regardless of processing time)
        return ScalingAction.None;
    }

    private async Task ExecuteScalingAction(AppMapping mapping, ScalingAction action, int scheduledReplicas, 
        RuntimeState state, CancellationToken cancellationToken)
    {
        var desiredReplicas = action == ScalingAction.Start && scheduledReplicas > 0 ? scheduledReplicas : mapping.DesiredReplicas;
        
        if (action == ScalingAction.Restart)
        {
            // Update restart tracking
            state.RestartAttemptCount++;
            state.LastRestartTime = DateTimeOffset.UtcNow;
            
            var attempt = new RestartAttempt
            {
                Timestamp = DateTimeOffset.UtcNow,
                Reason = "Messages present but no consumers detected",
                AttemptNumber = state.RestartAttemptCount,
                Success = false // Will be updated by ActionExecutor if successful
            };
            state.RestartHistory.Add(attempt);
            
            // Keep only last 10 restart attempts in history
            if (state.RestartHistory.Count > 10)
            {
                state.RestartHistory.RemoveAt(0);
            }
        }
        
        var actionType = action switch
        {
            ScalingAction.Start => ActionType.Start,
            ScalingAction.Stop => ActionType.Stop,
            ScalingAction.Restart => ActionType.Restart,
            _ => ActionType.None
        };
        
        if (actionType != ActionType.None)
        {
            var success = await _actions.ExecuteAsync(mapping, actionType, desiredReplicas, mapping.NotifyEmails, _options.CooldownMinutes, cancellationToken);
            
            // Update restart attempt success status
            if (action == ScalingAction.Restart && state.RestartHistory.Any())
            {
                state.RestartHistory.Last().Success = success;
                
                // Reset attempt counter on successful restart
                if (success)
                {
                    _logger.LogInformation("Restart successful for {App}, resetting attempt counter", mapping.ContainerApp);
                    state.RestartAttemptCount = 0;
                }
            }
        }
    }

    private async Task CheckLongProcessingMessages(AppMapping mapping, RuntimeState state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alertConfig = _options.MessageProcessingAlerts;
        
        foreach (var kvp in state.QueueConsumerStatus)
        {
            var queueName = kvp.Key;
            var queueState = kvp.Value;
            
            // Skip if no messages or no processing start time tracked
            if (queueState.MessageCount == 0 || !queueState.FirstMessageSeenAt.HasValue)
                continue;
                
            var processingDuration = now - queueState.FirstMessageSeenAt.Value;
            var shouldAlert = false;
            var alertMinutes = 0;
            
            // Determine if we should send an alert
            if (queueState.ProcessingAlertCount == 0)
            {
                // First alert
                if (processingDuration.TotalMinutes >= alertConfig.FirstAlertMinutes)
                {
                    shouldAlert = true;
                    alertMinutes = alertConfig.FirstAlertMinutes;
                }
            }
            else
            {
                // Follow-up alerts
                if (queueState.ProcessingAlertCount < alertConfig.MaxAlerts && 
                    queueState.LastProcessingAlert.HasValue)
                {
                    var timeSinceLastAlert = now - queueState.LastProcessingAlert.Value;
                    if (timeSinceLastAlert.TotalMinutes >= alertConfig.FollowupIntervalMinutes)
                    {
                        shouldAlert = true;
                        alertMinutes = alertConfig.FirstAlertMinutes + (queueState.ProcessingAlertCount * alertConfig.FollowupIntervalMinutes);
                    }
                }
            }
            
            if (shouldAlert)
            {
                queueState.ProcessingAlertCount++;
                queueState.LastProcessingAlert = now;
                
                _logger.LogWarning("Long message processing detected in queue {Queue} - {Duration} minutes (Alert #{Count})", 
                    queueName, (int)processingDuration.TotalMinutes, queueState.ProcessingAlertCount);
                
                await SendLongProcessingAlert(mapping, queueName, queueState, (int)processingDuration.TotalMinutes, alertMinutes, cancellationToken);
            }
        }
    }
    
    private async Task SendLongProcessingAlert(AppMapping mapping, string queueName, QueueConsumerState queueState, 
        int actualMinutes, int thresholdMinutes, CancellationToken cancellationToken)
    {
        var alertEmails = _options.MessageProcessingAlerts.AlertEmails.Any() 
            ? _options.MessageProcessingAlerts.AlertEmails 
            : mapping.NotifyEmails;
            
        if (!alertEmails.Any())
        {
            _logger.LogWarning("No alert emails configured for long processing alert - queue {Queue}", queueName);
            return;
        }
        
        var subject = $"Long Message Processing Alert: {queueName} - {actualMinutes} minutes";
        var body = $"<h3>⏱️ Long Message Processing Alert</h3>";
        body += $"<p><strong>Queue:</strong> {queueName}</p>";
        body += $"<p><strong>Container App:</strong> {mapping.ContainerApp}</p>";
        body += $"<p><strong>Resource Group:</strong> {mapping.ResourceGroup}</p>";
        body += $"<p><strong>Processing Duration:</strong> {actualMinutes} minutes</p>";
        body += $"<p><strong>Alert Threshold:</strong> {thresholdMinutes} minutes</p>";
        body += $"<p><strong>Alert Number:</strong> {queueState.ProcessingAlertCount} of {_options.MessageProcessingAlerts.MaxAlerts}</p>";
        body += $"<p><strong>Message Count:</strong> {queueState.MessageCount}</p>";
        body += $"<p><strong>Active Consumers:</strong> {(queueState.HasActiveConsumers ? "Yes" : "No")}</p>";
        
        if (queueState.LastConsumerSeen.HasValue)
        {
            body += $"<p><strong>Last Consumer Activity:</strong> {queueState.LastConsumerSeen:g}</p>";
        }
        
        body += $"<p><strong>Time (UTC):</strong> {DateTimeOffset.UtcNow:u}</p>";
        
        if (queueState.ProcessingAlertCount >= _options.MessageProcessingAlerts.MaxAlerts)
        {
            body += "<p><strong style='color: orange;'>ℹ️ Maximum alerts reached for this processing session.</strong></p>";
            body += "<p>No further alerts will be sent until queue is empty and messages reappear.</p>";
        }
        
        body += "<hr>";
        body += "<p><em>This is a monitoring alert only. No automatic action has been taken.</em></p>";
        body += "<p><em>Support team review recommended to determine if intervention is needed.</em></p>";
        
        try
        {
            await _notify.SendAsync(alertEmails, subject, body, cancellationToken);
            _logger.LogInformation("Sent long processing alert for queue {Queue} to {Recipients}", 
                queueName, string.Join(", ", alertEmails));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send long processing alert for queue {Queue}", queueName);
        }
    }
}


