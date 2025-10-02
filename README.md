# ContainerManager.Service

A production-ready container management service that automatically manages Azure Container Apps based on TIBCO EMS queue activity.

## Overview

ContainerManager.Service monitors TIBCO EMS queues and automatically manages Azure Container Apps lifecycle:
- **Restarts** containers when messages exist without receivers
- **Stops** containers after configurable idle timeout
- **Publishes** email notifications for all status changes and errors
- **Three-layer safety net** prevents operations from hanging indefinitely

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  MonitoringWorker                            │
│                  (Background Service)                         │
└───────────┬─────────────────────────────────────────────────┘
            │
            │ Every 30s (configurable)
            ▼
    ┌───────────────┐
    │ EmsQueueMonitor│──────► TIBCO EMS (SSL/TLS supported)
    └───────────────┘
            │
            ▼
    ┌───────────────┐
    │ DecisionEngine │ (Business Rules)
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
1. Restart Azure Container App (Stop → Wait 5s → Start)
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

---

## Configuration

### Quick Start

See `appsettings.json` for a complete configuration template with all available settings.

### appsettings.json Example

```json
{
  "ManagerSettings": {
    "PollingIntervalSeconds": 30,
    "IdleTimeoutMinutes": 10,
    "RestartVerificationTimeoutMinutes": 5,
    "RestartDelaySeconds": 5,
    "OperationTimeoutMinutes": 10,
    "StuckOperationCleanupMinutes": 15,
    "QueueContainerMappings": {
      "container-app-1": ["queue.app1.requests", "queue.app1.events"],
      "container-app-2": ["queue.app2.requests"]
    },
    "NotificationEmailRecipient": "ops-team@example.com",
    "NotifyOnSuccess": false,
    "NotifyOnWarning": true,
    "NotifyOnFailure": true
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
    "ManagedIdentityClientId": "your-managed-identity-client-id"
  }
}
```

### Configuration Options

#### ManagerSettings

**Polling & Timing:**
- `PollingIntervalSeconds` (default: 30, range: 1-3600) - How often to check queues
- `IdleTimeoutMinutes` (default: 10, range: 1-1440) - How long queues must be idle before stopping container
- `RestartVerificationTimeoutMinutes` (default: 5, range: 1-60) - How long to wait for receivers after restart
- `RestartDelaySeconds` (default: 5, range: 1-300) - Delay between stop and start during restart

**Operation Protection:**
- `OperationTimeoutMinutes` (default: 10, range: 1-60) - Individual operation timeout before forced cancellation
- `StuckOperationCleanupMinutes` (default: 15, range: 1-120) - Force cleanup if operation still tracked after this duration

**Mappings & Notifications:**
- `QueueContainerMappings` (required) - Map container apps to their queues
- `NotificationEmailRecipient` (required) - Email address(es) for notifications (supports multiple: "ops@example.com;oncall@example.com")

**Notification Levels** (control alert noise):
- `NotifyOnSuccess` (default: false) - Send notifications for successful operations
  - `false`: Reduces noise, only logs SUCCESS events
  - `true`: Sends email on every successful restart/stop
- `NotifyOnWarning` (default: true) - Send notifications for warning situations
  - Restart WARNING: Container restarted but no receivers detected (may indicate app failure)
- `NotifyOnFailure` (default: true) - Send notifications for critical failures
  - Restart FAILURE: Container restart failed, messages stuck
  - Stop FAILURE: Container stop failed, resources wasted

**Recommended Settings:**
```json
// Production - Only alert on problems
"NotifyOnSuccess": false,  // Skip routine operations
"NotifyOnWarning": true,   // Alert on partial failures
"NotifyOnFailure": true    // Alert on critical issues

// Development - See everything
"NotifyOnSuccess": true,
"NotifyOnWarning": true,
"NotifyOnFailure": true
```

**Configuration Validation:**
The service validates timeout relationships at startup:
- ✅ `StuckOperationCleanupMinutes` must be > `OperationTimeoutMinutes`
- ⚠️ `OperationTimeoutMinutes` should be ≥ `RestartVerificationTimeoutMinutes + 5` (normal restart takes 9-15 min)

Invalid configurations will prevent service startup with clear error messages.

#### EmsSettings

- `ServerUrl` (required) - TIBCO EMS server URL (tcp:// or ssl://)
- `Username` (required) - EMS username
- `Password` (required) - EMS password
- `NotificationQueueName` (default: "NOTIFICATION.QUEUE") - Queue for publishing notifications

**SSL/TLS Configuration (optional):**
- `SslTargetHostName` - Override hostname for certificate validation
- `SslTrace` - Enable SSL debugging
- `ClientCertificatePath` - Path to client certificate (.p12)
- `ClientCertificatePassword` - Client certificate password
- `TrustStorePath` - Path to server certificate trust store
- `VerifyHostName` (default: true) - Toggle hostname verification
- `VerifyServerCertificate` (default: true) - Toggle certificate verification

#### AzureSettings

- `SubscriptionId` (required) - Azure subscription ID
- `ResourceGroupName` (required) - Resource group containing Container Apps
- `ManagedIdentityClientId` (required) - Client ID of the user-assigned managed identity

---

## Deployment

### Prerequisites
- .NET 8.0 SDK
- Access to TIBCO EMS server
- Access to Azure Container Apps
- User-assigned managed identity with appropriate permissions:
  - `Microsoft.App/containerApps/read`
  - `Microsoft.App/containerApps/stop/action`
  - `Microsoft.App/containerApps/start/action`

### Local Development

```bash
dotnet build
dotnet run
```

### Docker Deployment

```bash
# Build Docker image
docker build -t container-manager:latest .

# Run with configuration
docker run --rm \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:latest

# Run with environment variable overrides
docker run --rm \
  -e ManagerSettings__IdleTimeoutMinutes=15 \
  -e ManagerSettings__OperationTimeoutMinutes=12 \
  -e EmsSettings__ServerUrl=tcp://ems-server:7222 \
  container-manager:latest
```

### Azure Container Apps Deployment

```bash
# Create user-assigned managed identity
az identity create \
  --name container-manager-identity \
  --resource-group your-rg

# Assign permissions (repeat for each container app to manage)
az role assignment create \
  --assignee <managed-identity-client-id> \
  --role "Azure Container Apps Administrator" \
  --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.App/containerApps/<app-name>

# Deploy container app
az containerapp create \
  --name container-manager \
  --resource-group your-rg \
  --image your-registry/container-manager:latest \
  --environment your-environment \
  --user-assigned <managed-identity-resource-id> \
  --set-env-vars \
    ManagerSettings__IdleTimeoutMinutes=10 \
    EmsSettings__ServerUrl=tcp://ems-server:7222 \
    AzureSettings__ManagedIdentityClientId=<client-id>
```

---

## Notifications

### Email Notification Format

Notifications are published to the configured EMS queue in JSON format ready for email processing:

```json
{
  "to": "ops-team@example.com",
  "subject": "Container Restart: SUCCESS - container-app-1",
  "message": "Container 'container-app-1' restarted successfully.\n\nReceivers detected on queues: queue.app1.requests\n\nTimestamp: 2025-10-01 14:03:45 UTC"
}
```

### Email Subjects

- `Container Restart: SUCCESS - {containerApp}` - Restart succeeded, receivers detected
- `Container Restart: WARNING - {containerApp}` - Restart succeeded, but no receivers after timeout
- `Container Restart: FAILURE - {containerApp}` - Restart operation failed
- `Container Stop: SUCCESS - {containerApp}` - Stop succeeded due to idle timeout
- `Container Stop: FAILURE - {containerApp}` - Stop operation failed

---

## Monitoring & Troubleshooting

### Logs

Logs are written to:
- **Console** (stdout) - Real-time monitoring
- **File** `logs/container-manager-YYYYMMDD.txt` - Rolling daily, retained 30 days

### Log Levels

- **Debug** - Queue states, receiver counts, operation durations
- **Information** - Actions decided, operations started/completed
- **Warning** - Restart succeeded but no receivers, timeout warnings, backoff notices
- **Error** - Azure API failures, EMS connection errors, exceptions

### Common Issues

#### "Operation already in progress, skipping"

**Normal:** Appears 6-20 times over 3-10 minutes during operation → Expected
**Problematic:** Appears 20+ times (10+ minutes), then force cleanup → Indicates hang

**Check:**
1. Operation duration logs: `"Operation for {Container} has been running for {Duration}"`
2. Timeout log: Should appear at 10 minutes (OperationTimeoutMinutes)
3. Force cleanup: Should NOT appear (indicates timeout failed)

#### Service not connecting to EMS

**Check:**
- EmsSettings: ServerUrl, Username, Password
- Network connectivity to EMS server
- EMS server logs for authentication errors
- SSL configuration if using ssl:// protocol

#### Containers not restarting

**Check:**
- Azure managed identity permissions
- Container app names in QueueContainerMappings match actual names
- Service logs for "Calling Azure API" and "Azure API confirmed" messages
- Azure Container Apps portal for operation status

#### Containers restarting but no receivers appear

**Check:**
- Container app actually starting (Azure Portal)
- Container app logs for startup errors
- Environment variables, connection strings in container app
- Increase `RestartVerificationTimeoutMinutes` if app takes >5 min to start

#### Notifications not being published

**Check:**
- NotificationQueueName exists in EMS
- EMS permissions for the user
- Service logs for "Failed to publish" errors
- Connection backoff warnings (service retries after 30s)

---

## Recent Improvements (2025-10-01)

### Fixed Issues

✅ **Container Operation Deadlock** - Operations no longer get stuck indefinitely
✅ **NotificationPublisher Deadlock Risk** - Network I/O moved outside locks
✅ **Missing Timeout Validation** - Invalid configurations blocked at startup
✅ **Default Configuration** - Added appsettings.json template

### Performance Improvements

- Reduced notification lock duration from seconds to milliseconds
- Added three-layer safety net (normal → timeout → force cleanup)
- Improved diagnostic logging for troubleshooting

See [CHANGELOG.md](CHANGELOG.md) for complete history.

---

## Project Structure

```
ContainerManager.Service/
├── Configuration/
│   ├── ManagerSettings.cs          # Manager settings with validation
│   ├── EmsSettings.cs              # EMS settings with SSL support
│   └── AzureSettings.cs            # Azure settings
├── Models/
│   ├── QueueInfo.cs                # Queue state from EMS
│   ├── QueueIdleState.cs           # Idle time tracking
│   ├── ContainerAction.cs          # Action enum (Restart/Stop/None)
│   └── EmailMessage.cs             # Email notification model
├── Services/
│   ├── EmsQueueMonitor.cs          # TIBCO EMS integration
│   ├── NotificationPublisher.cs    # EMS notification publisher
│   ├── ContainerManager.cs         # Azure Container Apps integration
│   └── DecisionEngine.cs           # Business rules engine
├── Workers/
│   └── MonitoringWorker.cs         # Main orchestration loop
├── Libs/
│   ├── TIBCO.EMS.dll              # TIBCO EMS client
│   └── TIBCO.EMS.ADMIN.dll        # TIBCO EMS admin API
├── appsettings.json                # Default configuration template
├── CHANGELOG.md                    # Version history
└── CLAUDE.md                       # Development guide
```

## Dependencies

- **.NET 8.0**
- **Azure.Identity** - Azure authentication
- **Azure.ResourceManager.AppContainers** - Azure Container Apps SDK
- **TIBCO.EMS** - TIBCO EMS client (included in Libs/)
- **Serilog** - Structured logging
- **Newtonsoft.Json** - JSON serialization

## License

Internal use only.
