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

The decision engine evaluates queues in the following order to protect active message processing:

### Rule 1: Protect Active Processing (NONE)
**Condition:** ANY queue has messages WITH receivers (actively processing)

**Action:** Do nothing - container is working normally

**Rationale:** Never interrupt active message processing. If any queue is actively working, don't restart the container even if other queues appear stuck.

### Rule 2: Restart Container (RESTART)
**Condition:** NO queues are actively processing AND ANY queue has messages WITHOUT receivers (stuck)

**Action:**
1. Restart Azure Container App (Stop → Wait → Start)
2. Wait up to 5 minutes for receivers to appear
3. Publish email notification:
   - SUCCESS: If receivers detected
   - WARNING: If no receivers after 5 minutes
   - FAILURE: If restart operation failed

**Rationale:** Messages are waiting but no consumers are connected. Container likely crashed or failed to start properly.

### Rule 3: Stop Container (STOP)
**Condition:** ALL queues have no messages (idle) AND at least one has receivers AND idle for configurable timeout (default 10 minutes)

**Action:**
1. Stop Azure Container App
2. Publish email notification:
   - SUCCESS: If stopped successfully
   - FAILURE: If stop operation failed

**Rationale:** No work to do, save resources by stopping idle containers.

---

## Multi-Queue Decision Logic

When multiple queues are mapped to a single container, the decision engine analyzes all queues together. Below are all 16 possible combinations for 2 queues:

**Legend:**
- **M+** = Messages > 0
- **M-** = Messages = 0
- **R+** = Receivers > 0
- **R-** = Receivers = 0

| # | QueueA | QueueB | Action | Reason |
|---|--------|--------|--------|--------|
| 1 | M+R+ | M+R+ | **NONE** | Both actively processing |
| 2 | M+R+ | M+R- | **NONE** | Protect QueueA processing (QueueB stuck but wait) |
| 3 | M+R+ | M-R+ | **NONE** | QueueA processing, QueueB idle |
| 4 | M+R+ | M-R- | **NONE** | QueueA processing, QueueB idle |
| 5 | M+R- | M+R+ | **NONE** | Protect QueueB processing (QueueA stuck but wait) |
| 6 | M+R- | M+R- | **RESTART** | Both stuck - no active processing |
| 7 | M+R- | M-R+ | **RESTART** | QueueA stuck, QueueB idle - safe to restart |
| 8 | M+R- | M-R- | **RESTART** | QueueA stuck, QueueB idle - safe to restart |
| 9 | M-R+ | M+R+ | **NONE** | QueueB processing, QueueA idle |
| 10 | M-R+ | M+R- | **RESTART** | QueueB stuck, QueueA idle - safe to restart |
| 11 | M-R+ | M-R+ | **STOP*** | Both idle with receivers → stop after timeout |
| 12 | M-R+ | M-R- | **STOP*** | QueueA idle with receivers → stop after timeout |
| 13 | M-R- | M+R+ | **NONE** | QueueB processing, QueueA completely idle |
| 14 | M-R- | M+R- | **RESTART** | QueueB stuck, QueueA idle - safe to restart |
| 15 | M-R- | M-R+ | **STOP*** | QueueB idle with receivers → stop after timeout |
| 16 | M-R- | M-R- | **NONE** | Both completely idle (container likely already stopped) |

***** STOP only triggers after IdleTimeoutMinutes has elapsed for ALL queues with receivers

### Key Behaviors:

**Active Processing Protection (Scenarios #2, #5):**
- If QueueA is processing messages but QueueB is stuck, the container will NOT restart
- This protects in-flight message processing from interruption
- QueueB messages will accumulate until QueueA finishes processing
- Once QueueA is idle, next poll cycle will detect QueueB stuck and trigger restart

**Stuck Queue Detection (Scenarios #6, #7, #8, #10, #14):**
- Only restarts when NO queues are actively processing
- Messages waiting with no receivers indicates container failure
- Safe to restart because no active work will be interrupted

**Idle Timeout (Scenarios #11, #12, #15):**
- Container stops only when ALL queues idle for full timeout period
- Timeout is per-queue - each queue tracks its own idle time
- If any queue becomes active during timeout, timer resets for all queues

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
    "ServerUrl": "tcp://ems-server:7222",  // or "ssl://ems-server:7243" for SSL
    "Username": "admin",
    "Password": "your-password",
    "NotificationQueueName": "NOTIFICATION.QUEUE",
    // Optional SSL configuration (only used with ssl:// protocol)
    "SslTargetHostName": "ems-server.example.com",
    "ClientCertificatePath": "/path/to/client-cert.p12",
    "TrustStorePath": "/path/to/truststore.jks",
    "VerifyHostName": true,
    "VerifyServerCertificate": true
  },
  "AzureSettings": {
    "SubscriptionId": "your-subscription-id",
    "ResourceGroupName": "your-resource-group",
    "ManagedIdentityClientId": "your-managed-identity-client-id"
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
- **ManagedIdentityClientId**: Client ID of the user-assigned managed identity (required)

## Deployment

### Prerequisites
- .NET 8.0 SDK
- Access to TIBCO EMS server
- Access to Azure Container Apps
- User-assigned managed identity with appropriate permissions

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