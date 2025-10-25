using ContainerManager.Service.Configuration;
using ContainerManager.Service.Health;
using ContainerManager.Service.Services;
using ContainerManager.Service.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Services.AddSerilog();

// Configuration with validation
builder.Services.AddOptions<ManagerSettings>()
    .Bind(builder.Configuration.GetSection("ManagerSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<EmsSettings>()
    .Bind(builder.Configuration.GetSection("EmsSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AzureSettings>()
    .Bind(builder.Configuration.GetSection("AzureSettings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register services
builder.Services.AddSingleton<IEmsQueueMonitor, EmsQueueMonitor>();
builder.Services.AddSingleton<IContainerManager, ContainerManager.Service.Services.ContainerManager>();
builder.Services.AddSingleton<IDecisionEngine, DecisionEngine>();
builder.Services.AddSingleton<INotificationPublisher, NotificationPublisher>();

// Register MonitoringWorker as singleton so health checks can access it
builder.Services.AddSingleton<MonitoringWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitoringWorker>());

// Register health checks with tags for filtering
builder.Services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" })
    .AddCheck<EmsReadinessHealthCheck>("readiness", tags: new[] { "ready" })
    .AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" });

var app = builder.Build();

// Map health check endpoints with tag filtering
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup")
});

try
{
    Log.Information("Starting ContainerManager.Service with health endpoints enabled");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}