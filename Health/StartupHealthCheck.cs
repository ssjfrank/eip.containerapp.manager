using ContainerManager.Service.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContainerManager.Service.Health;

/// <summary>
/// Startup health check that verifies the MonitoringWorker has completed initialization.
/// Returns Unhealthy until initialization attempts are complete.
/// This is used by Azure Container Apps startup probe to determine when the container is ready to start.
/// </summary>
public class StartupHealthCheck : IHealthCheck
{
    private readonly MonitoringWorker _monitoringWorker;

    public StartupHealthCheck(MonitoringWorker monitoringWorker)
    {
        _monitoringWorker = monitoringWorker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check if MonitoringWorker has completed initialization phase
        if (_monitoringWorker.IsInitializationComplete)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Service initialization completed"));
        }
        else
        {
            // Return Unhealthy during initialization
            // This tells Azure Container Apps to wait before considering the container ready
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Service is still initializing"));
        }
    }
}
