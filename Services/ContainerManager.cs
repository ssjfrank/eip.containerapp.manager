using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using ContainerManager.Service.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContainerManager.Service.Services;

public class ContainerManager : IContainerManager
{
    private readonly ILogger<ContainerManager> _logger;
    private readonly AzureSettings _azureSettings;
    private readonly ManagerSettings _managerSettings;
    private readonly IEmsQueueMonitor _emsMonitor;
    private ArmClient? _armClient;
    private ContainerAppCollection? _containerApps;
    private bool _disposed;

    public ContainerManager(
        ILogger<ContainerManager> logger,
        IOptions<AzureSettings> azureSettings,
        IOptions<ManagerSettings> managerSettings,
        IEmsQueueMonitor emsMonitor)
    {
        _logger = logger;
        _azureSettings = azureSettings.Value;
        _managerSettings = managerSettings.Value;
        _emsMonitor = emsMonitor;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing Azure Container Apps client with user-assigned managed identity {ClientId}",
                _azureSettings.ManagedIdentityClientId);

            // Use DefaultAzureCredential which works both locally and in Azure
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = _azureSettings.ManagedIdentityClientId
            });

            _armClient = new ArmClient(credential, _azureSettings.SubscriptionId);

            var subscription = await _armClient.GetDefaultSubscriptionAsync(cancellationToken);
            var resourceGroup = await subscription.GetResourceGroups()
                .GetAsync(_azureSettings.ResourceGroupName, cancellationToken);

            _containerApps = resourceGroup.Value.GetContainerApps();

            _logger.LogInformation("Azure Container Apps client initialized for subscription {SubscriptionId}, resource group {ResourceGroup}",
                _azureSettings.SubscriptionId, _azureSettings.ResourceGroupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Container Apps client");
            throw;
        }
    }

    public async Task RestartAsync(string containerAppName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_containerApps == null)
            {
                await InitializeAsync(cancellationToken);
            }

            _logger.LogInformation("Restarting container app {ContainerAppName}", containerAppName);

            // Get the container app resource
            var containerAppResourceId = ContainerAppResource.CreateResourceIdentifier(
                _azureSettings.SubscriptionId,
                _azureSettings.ResourceGroupName,
                containerAppName);

            var containerApp = _armClient.GetContainerAppResource(containerAppResourceId);

            // Stop the container app
            _logger.LogInformation("Stopping container app {ContainerAppName}", containerAppName);
            await containerApp.StopAsync(WaitUntil.Completed, cancellationToken);
            _logger.LogInformation("Container app {ContainerAppName} stopped", containerAppName);

            // Wait for Azure Resource Manager to propagate changes
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            // Configurable delay between stop and start
            var restartDelay = TimeSpan.FromSeconds(_managerSettings.RestartDelaySeconds);
            _logger.LogDebug("Waiting {RestartDelay} before starting back up", restartDelay);

            try
            {
                await Task.Delay(restartDelay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Restart operation cancelled during restart delay for {ContainerAppName}", containerAppName);
                throw;
            }

            // Start the container app
            _logger.LogInformation("Starting container app {ContainerAppName}", containerAppName);
            await containerApp.StartAsync(WaitUntil.Completed, cancellationToken);
            _logger.LogInformation("Container app {ContainerAppName} restarted successfully", containerAppName);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Restart operation cancelled for {ContainerAppName}", containerAppName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart container app {ContainerAppName}", containerAppName);
            throw;
        }
    }

    public async Task StopAsync(string containerAppName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_containerApps == null)
            {
                await InitializeAsync(cancellationToken);
            }

            _logger.LogInformation("Stopping container app {ContainerAppName}", containerAppName);

            var containerApp = await _containerApps!.GetAsync(containerAppName, cancellationToken);

            // Use the proper Stop method instead of scaling to 0
            await containerApp.Value.StopAsync(WaitUntil.Completed, cancellationToken);

            _logger.LogInformation("Container app {ContainerAppName} stopped successfully", containerAppName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop container app {ContainerAppName}", containerAppName);
            throw;
        }
    }

    public async Task<bool> WaitForReceiversAsync(List<string> queueNames, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var pollInterval = TimeSpan.FromSeconds(10);

        _logger.LogInformation("Waiting up to {Timeout} for receivers on queues: {QueueNames}",
            timeout, string.Join(", ", queueNames));

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            try
            {
                // Check if EMS is connected before polling
                if (!_emsMonitor.IsConnected)
                {
                    _logger.LogWarning("EMS not connected during receiver wait, attempting reconnect");
                    try
                    {
                        await _emsMonitor.InitializeAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to reconnect to EMS during receiver wait");
                        // Continue trying - don't give up yet
                        try
                        {
                            await Task.Delay(pollInterval, cancellationToken);
                        }
                        catch (TaskCanceledException)
                        {
                            return false;
                        }
                        continue;
                    }
                }

                bool allHaveReceivers = true;

                foreach (var queueName in queueNames)
                {
                    var receiverCount = await _emsMonitor.GetReceiverCountAsync(queueName, cancellationToken);

                    if (receiverCount == 0)
                    {
                        allHaveReceivers = false;
                        break;
                    }
                }

                if (allHaveReceivers)
                {
                    _logger.LogInformation("All queues have receivers after {Elapsed}", DateTime.UtcNow - startTime);
                    return true;
                }

                try
                {
                    await Task.Delay(pollInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking for receivers during wait");
                try
                {
                    await Task.Delay(pollInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }
        }

        _logger.LogWarning("Timeout waiting for receivers on queues after {Timeout}", timeout);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger.LogDebug("Disposing ContainerManager resources");

        // ArmClient doesn't implement IDisposable/IAsyncDisposable
        // but we clear references to allow GC
        _armClient = null;
        _containerApps = null;

        _disposed = true;

        await Task.CompletedTask;
    }
}