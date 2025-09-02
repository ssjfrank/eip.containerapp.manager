using Azure.Communication.Email;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using ContainerApp.Manager;
using ContainerApp.Manager.Azure;
using ContainerApp.Manager.Config;
using ContainerApp.Manager.Control;
using ContainerApp.Manager.Ems;
using ContainerApp.Manager.Notifications;
using ContainerApp.Manager.Scheduling;
using ContainerApp.Manager.State;
using Microsoft.Extensions.Options;
using Quartz;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Key Vault (optional) configuration source
var keyVaultUri = builder.Configuration.GetValue<string>("KeyVault:Uri");
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
    builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
}

// Options
builder.Services.AddOptions<MonitorOptions>()
    .Bind(builder.Configuration.GetSection("Monitor"))
    .Validate(o => o.Mappings.Count > 0, "At least one mapping must be configured");

builder.Services.AddOptions<EmsOptions>()
    .Bind(builder.Configuration.GetSection("Ems"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "EMS ConnectionString is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "EMS Username is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "EMS Password is required");

// Azure clients
builder.Services.AddSingleton(provider =>
{
    var cfg = builder.Configuration;
    var conn = cfg.GetValue<string>("Storage:ConnectionString");
    if (!string.IsNullOrWhiteSpace(conn))
    {
        return new BlobServiceClient(conn);
    }
    var accountUrl = cfg.GetValue<string>("Storage:AccountUrl");
    if (!string.IsNullOrWhiteSpace(accountUrl))
    {
        return new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
    }
    throw new InvalidOperationException("Configure Storage:ConnectionString or Storage:AccountUrl");
});
builder.Services.AddSingleton(provider =>
{
    var cfg = builder.Configuration;
    var conn = cfg.GetValue<string>("Storage:ConnectionString");
    if (!string.IsNullOrWhiteSpace(conn))
    {
        return new TableServiceClient(conn);
    }
    var accountUrl = cfg.GetValue<string>("Storage:AccountUrl");
    if (!string.IsNullOrWhiteSpace(accountUrl))
    {
        return new TableServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
    }
    throw new InvalidOperationException("Configure Storage:ConnectionString or Storage:AccountUrl");
});
builder.Services.AddSingleton(provider =>
{
    var acsConn = builder.Configuration.GetValue<string>("Acs:ConnectionString");
    return string.IsNullOrWhiteSpace(acsConn)
        ? new EmailClient(new Uri("https://contoso.invalid"), new DefaultAzureCredential())
        : new EmailClient(acsConn);
});

// Services
builder.Services.AddSingleton<IContainerAppManager, ContainerAppManager>();
builder.Services.AddSingleton<IEmsClient, EmsClient>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IScheduleEvaluator, ScheduleEvaluator>();
builder.Services.AddSingleton<IStateStore, TableStateStore>();
builder.Services.AddSingleton<ActionExecutorService>();
builder.Services.AddHostedService<LeaderElectionService>();
builder.Services.AddHostedService<DecisionEngineService>();
builder.Services.AddHostedService<Worker>();

// Quartz scheduler
builder.Services.AddQuartz(q => { });
builder.Services.AddQuartzHostedService(options => { options.WaitForJobsToComplete = false; });

// OpenTelemetry (traces + metrics) with Azure Monitor exporter
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ContainerApp.Manager"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("ContainerApp.Manager")
        .AddAzureMonitorTraceExporter())
    .WithMetrics(m => m
        .AddRuntimeInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddAzureMonitorMetricExporter());

// Minimal API for health endpoints
builder.Services.AddRouting();

var app = builder.Build();

// Health endpoints
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (ILeaderElectionService leader) =>
{
    return leader.IsLeader ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});

app.Run();
