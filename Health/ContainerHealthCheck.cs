using ContainerManager.Service.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContainerManager.Service.Health;

public class ContainerHealthCheck : IHealthCheck
{
    private readonly IEmsQueueMonitor _emsMonitor;

    public ContainerHealthCheck(IEmsQueueMonitor emsMonitor)
    {
        _emsMonitor = emsMonitor;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if EMS is connected
            if (!_emsMonitor.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Cannot connect to TIBCO EMS");
            }

            // Try to get queues
            var queues = await _emsMonitor.GetAllQueuesAsync(cancellationToken);

            if (queues == null)
            {
                return HealthCheckResult.Degraded("Connected to EMS but cannot retrieve queues");
            }

            return HealthCheckResult.Healthy($"ContainerManager operational, monitoring {queues.Count} queues");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }
}