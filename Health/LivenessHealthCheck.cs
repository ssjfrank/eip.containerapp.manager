using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContainerManager.Service.Health;

/// <summary>
/// Liveness health check that always returns healthy if the application is running.
/// This is used by Azure Container Apps liveness probe to determine if the container should be restarted.
/// Only critical, unrecoverable failures should cause this to return unhealthy.
/// </summary>
public class LivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Liveness check: As long as we can execute this code, the app is alive
        // Only critical failures (like out of memory, deadlock) would prevent this from running
        return Task.FromResult(HealthCheckResult.Healthy("Application is running"));
    }
}
