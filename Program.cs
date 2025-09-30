using ContainerManager.Service.Configuration;
using ContainerManager.Service.Health;
using ContainerManager.Service.Services;
using ContainerManager.Service.Workers;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

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

// Register worker
builder.Services.AddHostedService<MonitoringWorker>();

// Optional: Health checks
var enableHealthChecks = builder.Configuration.GetValue<bool>("EnableHealthChecks");
if (enableHealthChecks)
{
    builder.Services.AddHealthChecks()
        .AddCheck<ContainerHealthCheck>("container_manager");
}

var host = builder.Build();

try
{
    Log.Information("Starting ContainerManager.Service");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}