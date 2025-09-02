using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;

namespace ContainerApp.Manager.Azure;

public interface IContainerAppManager
{
    Task<ContainerAppStatus> GetStatusAsync(string resourceGroup, string containerAppName, CancellationToken cancellationToken);
    Task StartAsync(string resourceGroup, string containerAppName, int desiredReplicas, CancellationToken cancellationToken);
    Task StopAsync(string resourceGroup, string containerAppName, CancellationToken cancellationToken);
    Task RestartAsync(string resourceGroup, string containerAppName, int desiredReplicas, CancellationToken cancellationToken);
}

public sealed class ContainerAppStatus
{
    public int? MinReplicas { get; init; }
}

public sealed class ContainerAppManager : IContainerAppManager
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ContainerAppManager> _logger;

    public ContainerAppManager(ILogger<ContainerAppManager> logger, ArmClient? armClient = null, TokenCredential? credential = null)
    {
        _logger = logger;
        _armClient = armClient ?? new ArmClient(credential ?? new DefaultAzureCredential(includeInteractiveCredentials: false));
    }

    public async Task<ContainerAppStatus> GetStatusAsync(string resourceGroup, string containerAppName, CancellationToken cancellationToken)
    {
        var sub = await _armClient.GetDefaultSubscriptionAsync(cancellationToken);
        var rg = await sub.GetResourceGroupAsync(resourceGroup, cancellationToken);
        var app = await rg.Value.GetContainerAppAsync(containerAppName, cancellationToken);
        var data = app.Value.Data;
        return new ContainerAppStatus { MinReplicas = data.Template?.Scale?.MinReplicas };
    }

    public async Task StartAsync(string resourceGroup, string containerAppName, int desiredReplicas, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Container App {App} in {RG} to {Replicas} replicas", containerAppName, resourceGroup, desiredReplicas);
        await ScaleAsync(resourceGroup, containerAppName, desiredReplicas, cancellationToken);
    }

    public async Task StopAsync(string resourceGroup, string containerAppName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Container App {App} in {RG}", containerAppName, resourceGroup);
        await ScaleAsync(resourceGroup, containerAppName, 0, cancellationToken);
    }

    public async Task RestartAsync(string resourceGroup, string containerAppName, int desiredReplicas, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restarting Container App {App} in {RG}", containerAppName, resourceGroup);
        await ScaleAsync(resourceGroup, containerAppName, 0, cancellationToken);
        // brief delay to allow scale down to propagate
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await ScaleAsync(resourceGroup, containerAppName, desiredReplicas, cancellationToken);
    }

    private async Task ScaleAsync(string resourceGroup, string containerAppName, int minReplicas, CancellationToken cancellationToken)
    {
        var sub = await _armClient.GetDefaultSubscriptionAsync(cancellationToken);
        var rg = await sub.GetResourceGroupAsync(resourceGroup, cancellationToken);
        var app = await rg.Value.GetContainerAppAsync(containerAppName, cancellationToken);
        var data = app.Value.Data;
        data.Template ??= new ContainerAppTemplate();
        data.Template.Scale ??= new ContainerAppScale();
        data.Template.Scale.MinReplicas = minReplicas;
        await app.Value.UpdateAsync(global::Azure.WaitUntil.Completed, data, cancellationToken);
    }
}


