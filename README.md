# ContainerManager.Service

A simplified container management service that automatically manages Azure Container Apps based on TIBCO EMS queue activity.

## Overview

ContainerManager.Service monitors TIBCO EMS queues and automatically manages Azure Container Apps lifecycle:
- **Restarts** containers when messages exist without receivers
- **Stops** containers after configurable idle timeout
- **Publishes** notifications for all status changes and errors

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  MonitoringWorker                            │
│                  (Background Service)                         │
└───────────┬─────────────────────────────────────────────────┘
            │
            │ Every 30s
            ▼
    ┌───────────────┐
    │ EmsQueueMonitor│──────► TIBCO EMS
    └───────────────┘
            │
            ▼
    ┌───────────────┐
    │ DecisionEngine │
    └───────────────┘
            │
            ├──► Restart ──► ContainerManager ──► Azure Container Apps
            ├──► Stop    ──► ContainerManager ──► Azure Container Apps
            └──► Notify  ──► NotificationPublisher ──► NOTIFICATION.QUEUE
```

## Business Rules

### Rule 1: Restart Container
**Condition:** Queue has messages (PendingMessageCount > 0) AND no receivers (ReceiverCount = 0)

**Action:**
1. Restart Azure Container App (scale to 0, then scale back up)
2. Wait up to 5 minutes for receivers to appear
3. Publish notification:
   - SUCCESS: If receivers detected
   - WARNING: If no receivers after 5 minutes
   - FAILURE: If restart operation failed

### Rule 2: Stop Container
**Condition:** Queue has no messages (PendingMessageCount = 0) for configurable timeout (default 10 minutes) AND has receivers (ReceiverCount > 0)

**Action:**
1. Stop Azure Container App (scale to 0)
2. Publish notification:
   - SUCCESS: If stopped successfully
   - FAILURE: If stop operation failed

### Rule 3: Multi-Queue Support
- One container can listen to multiple queues
- **Restart:** If ANY queue has messages without receivers
- **Stop:** Only if ALL queues are idle for the timeout period
- **Skip:** If ANY queue has messages with receivers (working normally)

### Rule 4: Container State Detection
Container state is determined by queue receivers:
- **Running:** ANY of its queues has ReceiverCount > 0
- **Stopped:** ALL of its queues have ReceiverCount = 0

## Configuration

### appsettings.json

```json
{
  "ManagerSettings": {
    "PollingIntervalSeconds": 30,
    "IdleTimeoutMinutes": 10,
    "RestartVerificationTimeoutMinutes": 5,
    "QueueContainerMappings": {
      "container-app-1": ["queue.app1.requests", "queue.app1.events"],
      "container-app-2": ["queue.app2.requests"]
    }
  },
  "EmsSettings": {
    "ServerUrl": "tcp://ems-server:7222",
    "Username": "admin",
    "Password": "your-password",
    "NotificationQueueName": "NOTIFICATION.QUEUE"
  },
  "AzureSettings": {
    "SubscriptionId": "your-subscription-id",
    "ResourceGroupName": "your-resource-group",
    "UseManagedIdentity": true,
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": ""
  },
  "EnableHealthChecks": false
}
```

### Configuration Options

#### ManagerSettings
- **PollingIntervalSeconds**: How often to check queues (default: 30)
- **IdleTimeoutMinutes**: How long queues must be idle before stopping container (default: 10)
- **RestartVerificationTimeoutMinutes**: How long to wait for receivers after restart (default: 5)
- **QueueContainerMappings**: Map container apps to their queues

#### EmsSettings
- **ServerUrl**: TIBCO EMS server URL
- **Username**: EMS username
- **Password**: EMS password
- **NotificationQueueName**: Queue name for publishing notifications (default: NOTIFICATION.QUEUE)

#### AzureSettings
- **SubscriptionId**: Azure subscription ID
- **ResourceGroupName**: Resource group containing Container Apps
- **UseManagedIdentity**: Use Managed Identity for authentication (recommended for Azure)
- **TenantId/ClientId/ClientSecret**: Service Principal credentials (if not using Managed Identity)

## Deployment

### Prerequisites
- .NET 8.0 SDK
- Access to TIBCO EMS server
- Access to Azure Container Apps
- Azure credentials (Managed Identity or Service Principal)

### Build

```bash
cd Sources/ContainerManager.Service
dotnet build
```

### Run Locally

```bash
cd Sources/ContainerManager.Service
dotnet run
```

### Docker Deployment

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "ContainerManager.Service.dll"]
```

Build and run:
```bash
dotnet publish -c Release -o publish
docker build -t container-manager:latest .
docker run -e ManagerSettings__IdleTimeoutMinutes=15 container-manager:latest
```

### Azure Container Apps Deployment

```bash
# Create container app
az containerapp create \
  --name container-manager \
  --resource-group your-rg \
  --image your-registry/container-manager:latest \
  --environment your-environment \
  --managed-identity system

# Configure environment variables
az containerapp update \
  --name container-manager \
  --resource-group your-rg \
  --set-env-vars \
    ManagerSettings__IdleTimeoutMinutes=10 \
    EmsSettings__ServerUrl=tcp://ems-server:7222 \
    AzureSettings__SubscriptionId=your-sub-id
```

## Notification Messages

Notifications are published to the configured EMS queue (default: NOTIFICATION.QUEUE) in JSON format:

```json
{
  "Timestamp": "2025-09-29T10:30:00Z",
  "ContainerApp": "container-app-1",
  "Action": "RESTART",
  "Status": "SUCCESS",
  "Message": "Container restarted successfully, receivers detected on queues: queue.app1.requests",
  "QueueName": "queue.app1.requests"
}
```

### Status Values
- **SUCCESS**: Operation completed successfully
- **WARNING**: Operation completed but with issues (e.g., no receivers after restart)
- **FAILURE**: Operation failed

### Action Values
- **RESTART**: Container restart operation
- **STOP**: Container stop operation

## Monitoring

### Logs

Logs are written to:
- Console (stdout)
- File: `logs/container-manager-YYYYMMDD.txt` (rolling daily)

### Log Levels
- **Debug**: Queue states, receiver counts
- **Information**: Actions decided, operations started/completed
- **Warning**: Restart succeeded but no receivers, timeout warnings
- **Error**: Azure API failures, EMS connection errors, exceptions

### Health Check (Optional)

Enable health checks by setting `EnableHealthChecks: true` in configuration.

Health check endpoint: `/health`

Returns:
- 200 OK: Service is healthy
- 503 Service Unavailable: Service is unhealthy

## Troubleshooting

### Issue: Service not connecting to EMS
**Solution:**
- Check EmsSettings ServerUrl, Username, Password
- Verify network connectivity to EMS server
- Check EMS server logs

### Issue: Containers not restarting
**Solution:**
- Verify Azure credentials (Managed Identity or Service Principal)
- Check Azure subscription and resource group names
- Verify container app names in QueueContainerMappings match actual names
- Check service logs for Azure API errors

### Issue: Containers restarting but no receivers appear
**Solution:**
- Check if container app is actually starting (Azure Portal)
- Verify container app configuration (environment variables, connection strings)
- Check container app logs for startup errors
- Increase RestartVerificationTimeoutMinutes if app takes longer to start

### Issue: Notifications not being published
**Solution:**
- Verify NotificationQueueName exists in EMS
- Check EMS permissions for the user
- Check service logs for publish errors

## Project Structure

```
ContainerManager.Service/
├── Models/
│   ├── QueueInfo.cs                    # Queue state from EMS
│   ├── ContainerQueueMapping.cs        # Container-to-queue mappings
│   ├── QueueIdleState.cs              # Idle time tracking
│   ├── ContainerAction.cs             # Action enum (Restart/Stop/None)
│   └── NotificationMessage.cs         # Notification model
├── Services/
│   ├── IEmsQueueMonitor.cs            # EMS interface
│   ├── EmsQueueMonitor.cs             # TIBCO EMS integration
│   ├── INotificationPublisher.cs      # Notification interface
│   ├── NotificationPublisher.cs       # EMS notification publisher
│   ├── IContainerManager.cs           # Azure interface
│   ├── ContainerManager.cs            # Azure Container Apps integration
│   ├── IDecisionEngine.cs             # Decision interface
│   └── DecisionEngine.cs              # Business rules engine
├── Workers/
│   └── MonitoringWorker.cs            # Main orchestration loop
├── Configuration/
│   ├── ManagerSettings.cs             # Manager configuration
│   ├── EmsSettings.cs                 # EMS configuration
│   └── AzureSettings.cs               # Azure configuration
├── Health/
│   └── ContainerHealthCheck.cs        # Health check endpoint
├── Program.cs                          # DI and startup
├── appsettings.json                    # Configuration
└── ContainerManager.Service.csproj     # Project file
```

## Dependencies

- **.NET 8.0**
- **Azure.Identity** - Azure authentication
- **Azure.ResourceManager.AppContainers** - Azure Container Apps SDK
- **TIBCO.EMS** - TIBCO EMS client (included in Libs/ directory)
- **Serilog** - Structured logging
- **Newtonsoft.Json** - JSON serialization

## TIBCO EMS DLLs

The TIBCO EMS client libraries are included in the `Libs/` directory:
- `TIBCO.EMS.dll` - Core TIBCO EMS client
- `TIBCO.EMS.ADMIN.dll` - TIBCO EMS admin API

These are automatically copied to the build output directory.

## License

Internal use only.