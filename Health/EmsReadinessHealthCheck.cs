using ContainerManager.Service.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContainerManager.Service.Health;

/// <summary>
/// Readiness health check that verifies EMS connectivity.
/// Returns Degraded (not Unhealthy) when EMS is disconnected to allow the service
/// to continue running and auto-recover when EMS comes back online.
/// This is used by Azure Container Apps readiness probe.
/// </summary>
public class EmsReadinessHealthCheck : IHealthCheck
{
    private readonly IEmsQueueMonitor _emsMonitor;

    public EmsReadinessHealthCheck(IEmsQueueMonitor emsMonitor)
    {
        _emsMonitor = emsMonitor;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check EMS connection status
        if (_emsMonitor.IsConnected)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("EMS connection is active"));
        }
        else
        {
            // Return Degraded (not Unhealthy) to allow service to continue running
            // and auto-recover when EMS comes back online.
            // Degraded still returns HTTP 200, so container won't be restarted.
            return Task.FromResult(
                HealthCheckResult.Degraded("EMS connection is not available, service will retry"));
        }
    }
}
