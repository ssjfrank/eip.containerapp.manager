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

            var subject = $"ACA {action}: {mapping.ContainerApp}";
            var body = $"<p>Action: {action}</p><p>App: {mapping.ContainerApp}</p><p>RG: {mapping.ResourceGroup}</p><p>DesiredReplicas: {desiredReplicas}</p><p>Time (UTC): {DateTimeOffset.UtcNow:u}</p>";
            await _notify.SendAsync(recipients, subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Action} for {App}", action, mapping.ContainerApp);
            state.LastAction = action.ToString();
            state.LastActionResult = $"Failed: {ex.GetType().Name}";
            await _state.SaveAsync(mapping.ContainerApp, state, cancellationToken);
            var subject = $"ACA {action} FAILED: {mapping.ContainerApp}";
            var body = $"<p>Action failed: {action}</p><p>App: {mapping.ContainerApp}</p><p>Error: {ex.Message}</p>";
            await _notify.SendAsync(recipients, subject, body, cancellationToken);
            return false;
        }
    }
}


