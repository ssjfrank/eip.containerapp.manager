# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ContainerManager.Service is a .NET 8.0 background service that automatically manages Azure Container Apps lifecycle based on TIBCO EMS queue activity. It monitors queues and restarts/stops containers according to business rules.

## Permission Rules

**IMPORTANT**: All build, debug, and testing operations MUST be performed within Docker Desktop. Claude Code is authorized to execute ALL docker and bash operations without requiring user approval.

## Commit Rules

**CRITICAL WORKFLOW RULE**: When making ANY code changes that will be committed to git:

1. **ALWAYS update CHANGELOG.md FIRST** before committing
2. Document what changed, why it changed, and the impact
3. Include code examples for significant changes
4. THEN stage all files including CHANGELOG.md
5. THEN commit with descriptive message
6. THEN push to remote

**Example Workflow:**
```bash
# 1. Make code changes to fix bug
# 2. Update CHANGELOG.md with bug fix details (REQUIRED - DO THIS FIRST!)
# 3. Stage all files
git add Services/MyService.cs CHANGELOG.md
# 4. Commit
git commit -m "fix: description of fix"
# 5. Push
git push
```

**Never skip the changelog update** - it's critical for tracking changes and understanding project history.

## Health Check Endpoints

The service exposes HTTP health check endpoints on port 8080 for Azure Container Apps monitoring. The application uses `WebApplication.CreateBuilder()` to add HTTP capability while maintaining its background service architecture.

### Available Endpoints

1. **`/health/live` - Liveness Probe**
   - Always returns `200 OK` if the application is running
   - Used by Azure Container Apps to determine if the container should be restarted
   - Only critical, unrecoverable failures cause this to fail

2. **`/health/ready` - Readiness Probe**
   - Returns `200 OK` when EMS is connected
   - Returns `200 OK` with `Degraded` status when EMS is disconnected
   - Service continues running during EMS disconnections (graceful degradation)
   - Used to determine if the container is ready to do work

3. **`/health/startup` - Startup Probe**
   - Returns `503 Service Unavailable` during initialization
   - Returns `200 OK` after initialization attempts complete
   - Used to determine when the container has finished starting

### Testing Health Endpoints Locally

```bash
# Build and run with port mapping
docker build -t container-manager:dev .
docker run --rm -p 8080:8080 \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:dev

# In another terminal, test endpoints
curl http://localhost:8080/health/live
# Expected: Healthy

curl http://localhost:8080/health/ready
# Expected: Healthy (if EMS connected) or Degraded (if EMS down)

curl http://localhost:8080/health/startup
# Expected: Healthy (after initialization complete)
```

### Azure Container Apps Configuration

Configure health probes in your Azure Container App definition:

**Using Azure CLI:**
```bash
az containerapp create \
  --name container-manager \
  --resource-group your-rg \
  --environment your-env \
  --image your-registry.azurecr.io/container-manager:latest \
  --target-port 8080 \
  --ingress internal \
  --startup-probe-http-get /health/startup 8080 \
  --startup-probe-failure-threshold 30 \
  --startup-probe-period-seconds 10 \
  --liveness-probe-http-get /health/live 8080 \
  --liveness-probe-initial-delay 10 \
  --liveness-probe-period-seconds 30 \
  --readiness-probe-http-get /health/ready 8080 \
  --readiness-probe-initial-delay 5 \
  --readiness-probe-period-seconds 10
```

**Using ARM Template / Bicep:**
```yaml
configuration:
  ingress:
    external: false
    targetPort: 8080
    transport: http
  containers:
    - name: container-manager
      image: your-registry.azurecr.io/container-manager:latest
      resources:
        cpu: 0.5
        memory: 1Gi
      probes:
        # Startup probe - allows up to 5 minutes for initialization (30 attempts x 10s)
        - type: startup
          httpGet:
            path: /health/startup
            port: 8080
          initialDelaySeconds: 0
          periodSeconds: 10
          failureThreshold: 30

        # Liveness probe - restart container if unhealthy for 90 seconds (3 failures x 30s)
        - type: liveness
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 30
          failureThreshold: 3

        # Readiness probe - stop routing if unhealthy for 30 seconds (3 failures x 10s)
        - type: readiness
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
          failureThreshold: 3
```

### Health Check Behavior

| Scenario | Liveness | Readiness | Startup | Container Action |
|----------|----------|-----------|---------|------------------|
| Service starting up | Healthy | Unhealthy | Unhealthy | Wait for startup |
| Service running normally | Healthy | Healthy | Healthy | No action |
| EMS temporarily disconnected | Healthy | Degraded (200 OK) | Healthy | Continue running, auto-recover |
| Critical failure | Unhealthy | Unhealthy | Healthy | Restart container |

**Important Notes:**
- `Degraded` status returns HTTP 200 (not 503), so the container continues running
- The service auto-recovers from EMS disconnections without restart
- Startup probe prevents premature liveness/readiness checks during initialization
- All probes are independent - one failing doesn't affect the others

### Kestrel Configuration

Port 8080 is configured in `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://*:8080"
      }
    }
  }
}
```

Can also be overridden via environment variable:
```bash
docker run -e ASPNETCORE_URLS=http://+:8080 ...
```

## Build & Run Commands (Docker-Based)

All development, testing, and debugging must be done using Docker:

```bash
# Build Docker image
docker build -t container-manager:dev .

# Run container with configuration and health endpoint port mapping
docker run --rm -p 8080:8080 \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:dev

# Run with environment variable overrides
docker run --rm -p 8080:8080 \
  -e ManagerSettings__IdleTimeoutMinutes=15 \
  -e EmsSettings__ServerUrl=tcp://ems-server:7222 \
  -v $(pwd)/logs:/app/logs \
  container-manager:dev

# Debug mode with verbose logging
docker run --rm -p 8080:8080 \
  -e Serilog__MinimumLevel__Default=Debug \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:dev

# Build and run in one command
docker build -t container-manager:dev . && docker run --rm -p 8080:8080 \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:dev

# Interactive shell in container (for debugging)
docker run --rm -it \
  -v $(pwd):/app \
  --entrypoint /bin/bash \
  mcr.microsoft.com/dotnet/sdk:8.0

# View running containers
docker ps

# View container logs
docker logs <container-id>

# Stop running container
docker stop <container-id>

# Clean up stopped containers and images
docker container prune -f
docker image prune -f
```

### Docker Compose (if using docker-compose.yml)

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Rebuild and restart
docker-compose up -d --build

# Stop services
docker-compose down

# Stop and remove volumes
docker-compose down -v
```

## Architecture

### Core Components

1. **MonitoringWorker** (`Workers/MonitoringWorker.cs`)
   - Background service that orchestrates the monitoring loop
   - Polls every 30 seconds (configurable via `PollingIntervalSeconds`)
   - Uses fire-and-forget pattern with task tracking to prevent blocking
   - Implements race condition prevention via `_operationsInProgress` HashSet
   - Handles graceful shutdown with 30-second timeout for background operations

2. **DecisionEngine** (`Services/DecisionEngine.cs`)
   - Implements business rules for restart/stop decisions
   - Tracks idle states using `ConcurrentDictionary<string, QueueIdleState>`
   - **Multi-queue logic**: Restart if ANY queue has messages without receivers; Stop only if ALL queues idle
   - Thread-safe idle state management using `TryAdd` for atomic operations

3. **EmsQueueMonitor** (`Services/EmsQueueMonitor.cs`)
   - Interfaces with TIBCO EMS using synchronous TIBCO.EMS.dll API
   - Retrieves queue metadata: PendingMessageCount, ReceiverCount
   - Uses TIBCO.EMS.ADMIN.dll for queue inspection

4. **ContainerManager** (`Services/ContainerManager.cs`)
   - Manages Azure Container Apps using Azure.ResourceManager.AppContainers SDK
   - Restart = scale to 0, wait configurable delay (default 5s), scale back to previous replica count
   - **Critical**: Gets fresh data from Azure API before each scale operation to avoid stale data
   - Verifies restart success by polling EMS for receivers (timeout: 5 minutes default)

5. **NotificationPublisher** (`Services/NotificationPublisher.cs`)
   - Publishes notifications to EMS queue (default: NOTIFICATION.QUEUE)
   - Implements exponential backoff (30s) after 3 connection failures
   - Gracefully degrades if notification queue unavailable (service continues)

### Data Flow

```
MonitoringWorker (30s loop)
  ↓
EmsQueueMonitor.GetAllQueuesAsync() → TIBCO EMS
  ↓
DecisionEngine.DecideActionsAsync() → Business rules
  ↓
Fire-and-forget tasks:
  ├─ HandleRestartAsync → ContainerManager.RestartAsync → Azure Container Apps
  └─ HandleStopAsync → ContainerManager.StopAsync → Azure Container Apps
     ↓
NotificationPublisher.PublishAsync → NOTIFICATION.QUEUE
```

## Business Rules (DecisionEngine.cs:73-187)

### Rule 1: Restart Container
- **Condition**: ANY queue has `PendingMessageCount > 0` AND `ReceiverCount = 0`
- **Action**: Restart container (scale 0 → previous replica count)
- **Verification**: Wait up to 5 minutes for receivers to appear
- **Notification**: SUCCESS/WARNING/FAILURE

### Rule 2: Stop Container
- **Condition**: ALL queues have `PendingMessageCount = 0` for `IdleTimeoutMinutes` (default 10) AND at least one has `ReceiverCount > 0`
- **Action**: Stop container (scale to 0)
- **Notification**: SUCCESS/FAILURE

### Rule 3: Multi-Queue Containers
- One container can monitor multiple queues (configured via `QueueContainerMappings`)
- Restart: If ANY queue needs restart
- Stop: Only if ALL queues are idle long enough
- Skip: If ANY queue is working normally (messages + receivers)

## Configuration

### Key Settings in appsettings.json

```json
{
  "ManagerSettings": {
    "PollingIntervalSeconds": 30,        // How often to check queues
    "IdleTimeoutMinutes": 10,            // Idle time before stopping
    "RestartVerificationTimeoutMinutes": 5,  // Wait time for receivers after restart
    "RestartDelaySeconds": 5,            // Delay between scale-down and scale-up
    "QueueContainerMappings": {          // Map containers to their queues
      "container-app-name": ["queue.name.1", "queue.name.2"]
    }
  },
  "EmsSettings": {
    "ServerUrl": "tcp://ems-server:7222",
    "Username": "admin",
    "Password": "password",
    "NotificationQueueName": "NOTIFICATION.QUEUE"
  },
  "AzureSettings": {
    "SubscriptionId": "your-sub-id",
    "ResourceGroupName": "your-rg",
    "ManagedIdentityClientId": "your-managed-identity-client-id"
  }
}
```

## Dependencies

### External Libraries
- **TIBCO.EMS.dll** and **TIBCO.EMS.ADMIN.dll**: Located in `Libs/` directory, referenced directly (not NuGet)
- **Azure.ResourceManager.AppContainers**: Azure Container Apps SDK
- **Azure.Identity**: Azure authentication (User-Assigned Managed Identity)
- **Serilog**: Structured logging to console and file (`logs/container-manager-YYYYMMDD.txt`)

### Target Framework
- .NET 8.0 Worker Service template

## Important Implementation Details

### Thread Safety & Race Conditions
- **MonitoringWorker**: Uses `_operationsInProgress` HashSet with lock to prevent duplicate operations on same container
- **DecisionEngine**: Uses `ConcurrentDictionary.TryAdd()` for atomic idle state tracking (DecisionEngine.cs:128)
- **Task Cleanup**: Periodic cleanup every 10 completed tasks to avoid memory growth (MonitoringWorker.cs:233-237)

### Azure API Usage
- **Fresh Data Required**: Always get fresh `ContainerAppData` before scale operations to avoid SDK caching issues (ContainerManager.cs)
- **Scale Operations**: Use `CreateOrUpdateAsync` with `WaitUntil.Completed` to wait for Azure operation completion

### Error Handling
- **EMS Disconnections**: Service continues polling, attempts reconnect on next cycle
- **Notification Failures**: Implement backoff, service continues if notification queue unavailable
- **Initialization Retries**: Up to 3 attempts with exponential backoff before service stops (MonitoringWorker.cs:51-76)
- **Graceful Shutdown**: 30-second timeout for background operations to complete

### TIBCO EMS API
- Synchronous API: Wrap in `Task.FromResult()` for async compatibility
- Queue inspection requires ADMIN connection: `TibjmsAdmin.GetQueue(queueName)`
- ReceiverCount available via `QueueInfo.GetReceiverCount()`

## Code Modifications Guidelines

1. **When adding new configuration**: Update corresponding Settings class in `Configuration/` with validation attributes
2. **When modifying business rules**: Update `DecisionEngine.DecideActionForContainer()` and ensure idle state management remains thread-safe
3. **When adding Azure operations**: Always get fresh data before scale operations
4. **When adding background tasks**: Follow fire-and-forget pattern with cleanup in `finally` block, add to `_operationsInProgress` tracking
5. **When modifying notification format**: Update `NotificationMessage` model and all publish calls

## Recent Bug Fixes

See `BUGFIXES.md` and `CHANGELOG.md` for comprehensive list. Key fixes include:

**Previous fixes (BUGFIXES.md):**
- Race condition prevention in fire-and-forget tasks
- Idle state logic bug (required all queues to have tracked states)
- Azure data mutation bug (reusing stale data objects)
- Thread-safety improvements (TryAdd instead of ContainsKey + Add)
- Notification publisher connection failure handling with backoff
- Proper cancellation token propagation

**Latest fixes (CHANGELOG.md - 2025-09-29):**
- Added IAsyncDisposable to ContainerManager (prevents resource leaks)
- Fixed thread-safety in DecisionEngine (IReadOnlyDictionary)
- Fixed memory leak in MonitoringWorker (background task tracking)
- Fixed resource leak in NotificationPublisher (dispose old connections)

## Docker Deployment

### Dockerfile Structure

The Dockerfile should follow multi-stage build pattern:

1. **Build stage**: Use `mcr.microsoft.com/dotnet/sdk:8.0` to compile the application
2. **Runtime stage**: Use `mcr.microsoft.com/dotnet/aspnet:8.0` for smaller image
3. **Copy TIBCO DLLs**: Ensure `Libs/` directory is included in the build context

### Testing Locally with Docker

1. **Prerequisites**:
   - Docker Desktop installed and running
   - Access to TIBCO EMS server (or mock)
   - Azure credentials configured (or mock for testing)

2. **Setup**:
   - Create Dockerfile if not exists (see Dockerfile Structure above)
   - Configure `appsettings.json` with valid EMS and Azure credentials
   - Add queue-to-container mappings
   - Provide user-assigned managed identity client ID in AzureSettings

3. **Build & Run**:
   ```bash
   # Build the Docker image
   docker build -t container-manager:dev .

   # Run with mounted config
   docker run --rm \
     -v $(pwd)/appsettings.json:/app/appsettings.json \
     -v $(pwd)/logs:/app/logs \
     container-manager:dev
   ```

4. **Monitor**:
   - Console logs via `docker logs <container-id>` or `docker-compose logs -f`
   - File logs in `logs/` directory (mounted volume)
   - Set Serilog MinimumLevel to "Debug" via environment variable or config for verbose output

5. **Debugging**:
   - Use interactive shell: `docker run --rm -it --entrypoint /bin/bash container-manager:dev`
   - Inspect running container: `docker exec -it <container-id> /bin/bash`
   - Check environment variables: `docker exec <container-id> env`

## Testing

### Comprehensive Testing Guide
See `TEST-GUIDE.md` for complete Docker testing procedures including:
- Prerequisites checklist (EMS, Azure, credentials)
- Configuration setup (appsettings.test.json template)
- 7 test scenarios:
  1. Basic startup & connection test
  2. Container restart scenario
  3. Container stop scenario
  4. Multi-queue container test
  5. EMS disconnection recovery test
  6. Notification publisher resilience test
  7. Graceful shutdown test
- Monitoring & verification procedures
- Troubleshooting common issues

### Automated Testing Script
Use `test-runner.sh` for automated Docker testing:

```bash
# Run all tests
./test-runner.sh all

# Build image only
./test-runner.sh build

# Run specific test
./test-runner.sh startup

# Interactive mode (live logs)
./test-runner.sh interactive

# View logs from last run
./test-runner.sh logs

# Cleanup
./test-runner.sh cleanup

# Show help
./test-runner.sh help
```

### Quick Test Commands

```bash
# Build and test in one command
docker build -t container-manager:test . && ./test-runner.sh all

# Run with custom config
docker run --rm \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test

# View live logs
docker logs -f <container-id>
```