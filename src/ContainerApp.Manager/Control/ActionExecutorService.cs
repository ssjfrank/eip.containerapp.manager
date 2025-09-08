using ContainerApp.Manager.Azure;
using ContainerApp.Manager.Config;
using ContainerApp.Manager.Notifications;
using ContainerApp.Manager.State;

namespace ContainerApp.Manager.Control;

public enum ActionType
{
    None,
    Start,
    Stop,
    Restart
}

public sealed class ActionExecutorService
{
    private readonly IContainerAppManager _aca;
    private readonly IStateStore _state;
    private readonly INotificationService _notify;
    private readonly ILogger<ActionExecutorService> _logger;

    public ActionExecutorService(IContainerAppManager aca, IStateStore state, INotificationService notify, ILogger<ActionExecutorService> logger)
    {
        _aca = aca;
        _state = state;
        _notify = notify;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(AppMapping mapping, ActionType action, int desiredReplicas, IEnumerable<string> recipients, int cooldownMinutes, CancellationToken cancellationToken)
    {
        var state = await _state.LoadAsync(mapping.ContainerApp, cancellationToken);
        
        // Handle special case for conflict notifications
        if (action == ActionType.None)
        {
            await SendConflictNotification(mapping, state, recipients, cancellationToken);
            return true;
        }
        
        if (state.CooldownUntil.HasValue && state.CooldownUntil.Value > DateTimeOffset.UtcNow)
        {
            _logger.LogInformation("Cooldown active for {App} until {Until}", mapping.ContainerApp, state.CooldownUntil);
            return false;
        }

        try
        {
            switch (action)
            {
                case ActionType.Start:
                    await _aca.StartAsync(mapping.ResourceGroup, mapping.ContainerApp, desiredReplicas, cancellationToken);
                    state.LastStart = DateTimeOffset.UtcNow;
                    break;
                case ActionType.Stop:
                    await _aca.StopAsync(mapping.ResourceGroup, mapping.ContainerApp, cancellationToken);
                    state.LastStop = DateTimeOffset.UtcNow;
                    break;
                case ActionType.Restart:
                    await _aca.RestartAsync(mapping.ResourceGroup, mapping.ContainerApp, desiredReplicas, cancellationToken);
                    state.LastRestart = DateTimeOffset.UtcNow;
                    break;
                default:
                    return false;
            }
            
            state.LastAction = action.ToString();
            state.LastActionResult = "Success";
            state.CooldownUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Max(0, cooldownMinutes));
            await _state.SaveAsync(mapping.ContainerApp, state, cancellationToken);

            await SendActionNotification(mapping, action, desiredReplicas, state, recipients, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Action} for {App}", action, mapping.ContainerApp);
            state.LastAction = action.ToString();
            state.LastActionResult = $"Failed: {ex.GetType().Name}";
            await _state.SaveAsync(mapping.ContainerApp, state, cancellationToken);
            
            await SendFailureNotification(mapping, action, ex, state, recipients, cancellationToken);
            return false;
        }
    }
    
    private async Task SendActionNotification(AppMapping mapping, ActionType action, int desiredReplicas, 
        RuntimeState state, IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        var subject = $"ACA {action}: {mapping.ContainerApp}";
        var body = $"<h3>Container App Action: {action}</h3>";
        body += $"<p><strong>App:</strong> {mapping.ContainerApp}</p>";
        body += $"<p><strong>Resource Group:</strong> {mapping.ResourceGroup}</p>";
        body += $"<p><strong>Desired Replicas:</strong> {desiredReplicas}</p>";
        body += $"<p><strong>Time (UTC):</strong> {DateTimeOffset.UtcNow:u}</p>";
        
        if (action == ActionType.Restart)
        {
            body += $"<p><strong>Restart Attempt:</strong> {state.RestartAttemptCount} of {mapping.MaxRestartAttempts}</p>";
            
            if (state.RestartHistory.Any())
            {
                body += "<h4>Recent Restart History:</h4><ul>";
                foreach (var attempt in state.RestartHistory.TakeLast(5))
                {
                    body += $"<li>Attempt {attempt.AttemptNumber} at {attempt.Timestamp:g} - {attempt.Reason} - {(attempt.Success ? "Success" : "Failed")}</li>";
                }
                body += "</ul>";
            }
        }
        
        await _notify.SendAsync(recipients, subject, body, cancellationToken);
    }
    
    private async Task SendFailureNotification(AppMapping mapping, ActionType action, Exception ex, 
        RuntimeState state, IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        var subject = $"ACA {action} FAILED: {mapping.ContainerApp}";
        var body = $"<h3>Container App Action Failed: {action}</h3>";
        body += $"<p><strong>App:</strong> {mapping.ContainerApp}</p>";
        body += $"<p><strong>Error:</strong> {ex.Message}</p>";
        body += $"<p><strong>Time (UTC):</strong> {DateTimeOffset.UtcNow:u}</p>";
        
        if (action == ActionType.Restart)
        {
            body += $"<p><strong>Restart Attempts:</strong> {state.RestartAttemptCount} of {mapping.MaxRestartAttempts}</p>";
            
            if (state.RestartAttemptCount >= mapping.MaxRestartAttempts)
            {
                body += "<p><strong style='color: red;'>⚠️ MAXIMUM RESTART ATTEMPTS REACHED</strong></p>";
                body += "<p>No further restart attempts will be made until manual intervention.</p>";
            }
        }
        
        await _notify.SendAsync(recipients, subject, body, cancellationToken);
    }
    
    private async Task SendConflictNotification(AppMapping mapping, RuntimeState state, 
        IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        var subject = $"ACA Restart Skipped - Multi-Queue Conflict: {mapping.ContainerApp}";
        var body = $"<h3>Container App Restart Skipped Due to Multi-Queue Conflict</h3>";
        body += $"<p><strong>App:</strong> {mapping.ContainerApp}</p>";
        body += $"<p><strong>Resource Group:</strong> {mapping.ResourceGroup}</p>";
        body += $"<p><strong>Reason:</strong> Other queues for this container app have active consumers</p>";
        body += $"<p><strong>Time (UTC):</strong> {DateTimeOffset.UtcNow:u}</p>";
        
        if (state.QueueConsumerStatus.Any())
        {
            body += "<h4>Queue Status:</h4><ul>";
            foreach (var kvp in state.QueueConsumerStatus)
            {
                var queueName = kvp.Key;
                var queueState = kvp.Value;
                body += $"<li><strong>{queueName}:</strong> {queueState.MessageCount} messages, ";
                body += $"Active Consumers: {(queueState.HasActiveConsumers ? "Yes" : "No")}";
                if (queueState.LastConsumerSeen.HasValue)
                {
                    body += $", Last Consumer Seen: {queueState.LastConsumerSeen:g}";
                }
                body += "</li>";
            }
            body += "</ul>";
        }
        
        await _notify.SendAsync(recipients, subject, body, cancellationToken);
    }
}


