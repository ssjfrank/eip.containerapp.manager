# ContainerManager.Service - Docker Testing Guide

Complete guide for testing ContainerManager.Service in a Docker environment with TIBCO EMS and Azure Container Apps already set up.

---

## Prerequisites Checklist

Before starting, ensure you have:

### Infrastructure
- [x] Docker Desktop installed and running
- [x] TIBCO EMS server accessible (host:port)
- [x] Azure Container Apps deployed and accessible
- [x] Network connectivity between Docker and EMS/Azure

### Credentials & Access
- [x] EMS username and password
- [x] Azure subscription ID
- [x] Azure resource group name
- [x] Azure authentication method chosen:
  - **Option A:** Managed Identity (recommended for Azure-hosted containers)
  - **Option B:** Service Principal (ClientId, ClientSecret, TenantId)

### EMS Queue Setup
- [x] Test queues created in EMS (e.g., `queue.app1.requests`, `queue.app1.events`)
- [x] Notification queue created (`NOTIFICATION.QUEUE`)
- [x] EMS user has permissions to read queue metadata and send messages

### Azure Container Apps Setup
- [x] Container apps deployed (e.g., `container-app-1`, `container-app-2`)
- [x] Container apps configured to listen to test queues
- [x] Container apps can be scaled (min/max replicas configured)

---

## Configuration Setup

### Step 1: Copy Configuration Template

Create a test configuration file:

```bash
cp appsettings.json appsettings.test.json
```

### Step 2: Configure appsettings.test.json

**IMPORTANT:** Update with your actual values:

```json
{
  "ManagerSettings": {
    "PollingIntervalSeconds": 30,
    "IdleTimeoutMinutes": 10,
    "RestartVerificationTimeoutMinutes": 5,
    "RestartDelaySeconds": 5,
    "QueueContainerMappings": {
      "YOUR-CONTAINER-APP-NAME": [
        "YOUR.QUEUE.NAME.1",
        "YOUR.QUEUE.NAME.2"
      ]
    }
  },
  "EmsSettings": {
    "ServerUrl": "tcp://YOUR-EMS-HOST:7222",
    "Username": "YOUR-EMS-USERNAME",
    "Password": "YOUR-EMS-PASSWORD",
    "NotificationQueueName": "NOTIFICATION.QUEUE"
  },
  "AzureSettings": {
    "SubscriptionId": "YOUR-AZURE-SUBSCRIPTION-ID",
    "ResourceGroupName": "YOUR-RESOURCE-GROUP",
    "UseManagedIdentity": false,
    "TenantId": "YOUR-TENANT-ID",
    "ClientId": "YOUR-CLIENT-ID",
    "ClientSecret": "YOUR-CLIENT-SECRET"
  },
  "EnableHealthChecks": false,
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/container-manager-.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

**Configuration Tips:**
- Set `Default` log level to `Debug` for verbose testing output
- Use short timeouts for faster testing: `PollingIntervalSeconds: 10`, `IdleTimeoutMinutes: 2`
- Start with one container-queue mapping to simplify testing

---

## Build Docker Image

```bash
# Build the image
docker build -t container-manager:test .

# Verify image created
docker images container-manager:test
```

**Expected Output:**
```
REPOSITORY          TAG       IMAGE ID       CREATED         SIZE
container-manager   test      <image-id>     X seconds ago   363MB
```

---

## Running Tests

### Test 1: Basic Startup & Connection Test

**Objective:** Verify service starts and connects to EMS and Azure

```bash
# Run container with test configuration
docker run --rm \
  --name cm-test \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test
```

**Expected Behavior:**
```
[INF] Starting ContainerManager.Service
[INF] All services initialized successfully
[INF] EMS connected to tcp://YOUR-EMS-HOST:7222
[INF] Azure Container Apps client initialized for subscription <sub-id>
[INF] DecisionEngine initialized with X container mappings
[INF] Notification publisher initialized for queue NOTIFICATION.QUEUE
[INF] ContainerManager MonitoringWorker starting
[DBG] Retrieving queue information from EMS
[DBG] Retrieved X queues, analyzing for actions
```

**Verify:**
- [x] No startup errors
- [x] EMS connection successful
- [x] Azure client initialized
- [x] Queues discovered
- [x] Logs written to `logs/` directory

**Troubleshooting:**
- If EMS connection fails: Check ServerUrl, Username, Password, network connectivity
- If Azure fails: Check SubscriptionId, ResourceGroupName, credentials
- If config validation fails: Check all required fields are populated

---

### Test 2: Container Restart Scenario

**Objective:** Test automatic container restart when messages exist without receivers

**Setup:**
1. Stop your container app (scale to 0) via Azure Portal
2. Send messages to the queue using EMS admin tool or producer app
3. Verify queue has `PendingMessageCount > 0` and `ReceiverCount = 0`

**Run Test:**
```bash
docker run --rm \
  --name cm-restart-test \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test
```

**Expected Behavior:**
```
[DBG] Retrieved X queues, analyzing for actions
[INF] Queue YOUR.QUEUE.NAME.1 has X messages with no receivers → Restart YOUR-CONTAINER-APP
[INF] Decision for YOUR-CONTAINER-APP: Restart
[WRN] Starting restart operation for container YOUR-CONTAINER-APP
[INF] Restarting container app YOUR-CONTAINER-APP
[INF] Container app YOUR-CONTAINER-APP scaled to 0
[DBG] Waiting 5s before scaling back up
[INF] Scaling up container app YOUR-CONTAINER-APP with MaxReplicas=X
[INF] Container app YOUR-CONTAINER-APP restarted successfully
[INF] Waiting up to 00:05:00 for receivers on queues: YOUR.QUEUE.NAME.1
[INF] All queues have receivers after 00:XX:XX
[INF] Container YOUR-CONTAINER-APP restarted successfully, receivers detected
[INF] Published notification: Container=YOUR-CONTAINER-APP, Action=RESTART, Status=SUCCESS
```

**Verify:**
- [x] Service detects messages without receivers
- [x] Container scaled to 0
- [x] Container scaled back up
- [x] Service waits for receivers to appear
- [x] SUCCESS notification published to NOTIFICATION.QUEUE
- [x] Container app actually restarted in Azure Portal

**Check Notification Queue:**
```bash
# Connect to EMS and browse NOTIFICATION.QUEUE
# Expected message:
{
  "Timestamp": "2025-09-29T...",
  "ContainerApp": "YOUR-CONTAINER-APP",
  "Action": "RESTART",
  "Status": "SUCCESS",
  "Message": "Container restarted successfully, receivers detected on queues: YOUR.QUEUE.NAME.1",
  "QueueName": "YOUR.QUEUE.NAME.1"
}
```

---

### Test 3: Container Stop Scenario

**Objective:** Test automatic container stop when queues idle for configured timeout

**Setup:**
1. Ensure container app is running (receivers connected to queues)
2. Stop sending messages to queues
3. Wait for `IdleTimeoutMinutes` (default 10 minutes, or 2 if you changed it)

**Run Test:**
```bash
docker run --rm \
  --name cm-stop-test \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test
```

**Expected Behavior:**
```
[DBG] Queue YOUR.QUEUE.NAME.1 idle for 00:10:00, waiting for 00:10:00
[DBG] Queue YOUR.QUEUE.NAME.2 idle for 00:10:00, waiting for 00:10:00
[INF] All queues for YOUR-CONTAINER-APP idle for 00:10:00 → Stop container
[INF] Decision for YOUR-CONTAINER-APP: Stop
[WRN] Starting stop operation for container YOUR-CONTAINER-APP
[INF] Stopping container app YOUR-CONTAINER-APP
[INF] Container app YOUR-CONTAINER-APP stopped successfully due to idle queues
[INF] Published notification: Container=YOUR-CONTAINER-APP, Action=STOP, Status=SUCCESS
```

**Verify:**
- [x] Service tracks idle time for each queue
- [x] Container only stops after ALL queues idle for timeout period
- [x] Container scaled to 0 in Azure Portal
- [x] SUCCESS notification published

---

### Test 4: Multi-Queue Container Test

**Objective:** Test multi-queue logic (restart if ANY needs restart, stop only if ALL idle)

**Setup:**
Configure container with multiple queues:
```json
"QueueContainerMappings": {
  "YOUR-CONTAINER-APP": [
    "queue.1",
    "queue.2",
    "queue.3"
  ]
}
```

**Test 4a: Restart with One Queue Having Messages**
1. Send messages to `queue.1` only
2. Ensure container is stopped

**Expected:** Container restarts because ANY queue has messages without receivers

**Test 4b: Stop Only When All Queues Idle**
1. Ensure container is running
2. Send messages to `queue.1` (has receivers)
3. Leave `queue.2` and `queue.3` idle

**Expected:** Container stays running because ANY queue has activity

**Test 4c: Stop After All Queues Idle**
1. Stop sending to all queues
2. Wait for idle timeout

**Expected:** Container stops only after ALL queues idle for timeout period

---

### Test 5: EMS Disconnection Recovery Test

**Objective:** Test service resilience when EMS server disconnects

**Run Test:**
```bash
docker run --rm \
  --name cm-ems-test \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test
```

**During Runtime:**
1. Let service run normally for 1-2 minutes
2. Stop EMS server or block network connection
3. Wait 1-2 minutes
4. Restart EMS server or restore connection

**Expected Behavior:**
```
[DBG] Retrieving queue information from EMS
[WRN] EMS connection check failed, attempting reconnect
[WRN] Failed to reconnect to EMS: <error details>
[INF] EMS connection re-established
[DBG] Retrieving queue information from EMS
[DBG] Retrieved X queues, analyzing for actions
```

**Verify:**
- [x] Service detects EMS disconnection
- [x] Service attempts automatic reconnection
- [x] Service resumes normal operation after reconnect
- [x] No service crash or hang

---

### Test 6: Notification Publisher Resilience Test

**Objective:** Test notification publishing with queue unavailable

**Setup:**
1. Delete or disable `NOTIFICATION.QUEUE` in EMS
2. Trigger a restart or stop operation

**Expected Behavior:**
```
[ERR] Failed to initialize notification publisher: <error>
[WRN] Initial notification publisher connection failed, will retry on publish
[INF] Container YOUR-CONTAINER-APP restarted successfully
[WRN] Notification publisher not initialized, attempting to reconnect
[ERR] Failed to initialize notification publisher
[WRN] Notification publisher not initialized and in backoff period, skipping notification
```

**After Recreating Queue:**
```
[WRN] Notification publisher not initialized, attempting to reconnect
[INF] Notification publisher initialized for queue NOTIFICATION.QUEUE
[INF] Published notification: Container=..., Action=..., Status=...
```

**Verify:**
- [x] Service continues monitoring even if notifications fail
- [x] Service implements exponential backoff (30 seconds after 3 failures)
- [x] Service recovers automatically when queue becomes available
- [x] Old connections properly disposed before reconnection (no resource leak)

---

### Test 7: Graceful Shutdown Test

**Objective:** Test clean shutdown with operations in progress

**Run Test:**
```bash
docker run --rm \
  --name cm-shutdown-test \
  -v $(pwd)/appsettings.test.json:/app/appsettings.json \
  -v $(pwd)/logs:/app/logs \
  container-manager:test
```

**During a Restart Operation:**
```bash
# In another terminal, stop the container
docker stop cm-shutdown-test
```

**Expected Behavior:**
```
[INF] Waiting for X background operations to complete
[INF] All background operations completed
[DBG] Disposing ContainerManager resources
[INF] Notification publisher closed
[INF] ContainerManager MonitoringWorker stopping
```

**Verify:**
- [x] Service waits up to 30 seconds for background operations
- [x] All resources properly disposed
- [x] No errors during shutdown
- [x] Container stops cleanly

---

## Monitoring & Verification

### View Logs in Real-Time

```bash
# Console logs (live)
docker logs -f cm-test

# File logs
tail -f logs/container-manager-*.txt
```

### Check Azure Container Apps

```bash
# Check replica count
az containerapp show \
  --name YOUR-CONTAINER-APP \
  --resource-group YOUR-RESOURCE-GROUP \
  --query "properties.template.scale" -o json
```

### Check EMS Queues

Use EMS Admin tool or command-line:
- Check `PendingMessageCount`
- Check `ReceiverCount`
- Browse `NOTIFICATION.QUEUE` for notifications

### Check Service Metrics

```bash
# View running containers
docker ps | grep container-manager

# View container resource usage
docker stats cm-test

# Inspect container details
docker inspect cm-test
```

---

## Common Issues & Troubleshooting

### Issue 1: Configuration Validation Failed
**Error:** `Failed to initialize after X attempts, stopping service`

**Solutions:**
- Check all required fields populated in appsettings.test.json
- Verify JSON syntax is valid
- Check ManagerSettings.QueueContainerMappings has at least one mapping

### Issue 2: EMS Connection Failed
**Error:** `Failed to initialize notification publisher: Connection refused`

**Solutions:**
- Verify EMS server is running: `telnet YOUR-EMS-HOST 7222`
- Check ServerUrl format: `tcp://host:port`
- Verify username/password
- Check firewall/network rules

### Issue 3: Azure Authentication Failed
**Error:** `Failed to initialize Azure Container Apps client`

**Solutions:**
- If using Service Principal: Verify TenantId, ClientId, ClientSecret are correct
- If using Managed Identity: Deploy to Azure environment with identity assigned
- Check Azure subscription is active
- Verify service principal has Contributor role on resource group

### Issue 4: Container Not Found
**Error:** `Container app YOUR-CONTAINER-APP not found`

**Solutions:**
- Verify container app name matches exactly (case-sensitive)
- Check container app exists in the resource group
- Verify resource group name is correct

### Issue 5: Queues Not Found
**Error:** `No queues found in EMS`

**Solutions:**
- Check queue names in QueueContainerMappings match actual EMS queue names
- Verify queues are created in EMS
- Check EMS user has permission to view queues

### Issue 6: Docker Network Issues
**Error:** `Connection timeout` or `No route to host`

**Solutions:**
- Use host.docker.internal for localhost services from container
- Update ServerUrl to: `tcp://host.docker.internal:7222`
- Or use Docker host networking: `docker run --network host ...`

---

## Performance Testing

### Load Test: Multiple Containers
Configure 5-10 container-queue mappings and monitor:
- Memory usage (should stay stable)
- CPU usage (should be low, spikes during operations)
- No memory leaks over 24 hours

### Concurrent Operations Test
Trigger multiple containers needing restart simultaneously:
- Verify _operationsInProgress prevents duplicates
- Check Azure API rate limiting doesn't cause failures
- Verify all containers eventually restart

---

## Test Completion Checklist

Before approving for production:

- [ ] Test 1: Basic startup successful
- [ ] Test 2: Container restart works
- [ ] Test 3: Container stop works
- [ ] Test 4: Multi-queue logic correct
- [ ] Test 5: EMS disconnection recovery works
- [ ] Test 6: Notification resilience works
- [ ] Test 7: Graceful shutdown works
- [ ] All notifications received in NOTIFICATION.QUEUE
- [ ] No resource leaks after 1+ hour runtime
- [ ] No errors in logs (except expected connection failures during recovery tests)
- [ ] Azure operations complete successfully
- [ ] Container apps scale correctly

---

## Automated Testing

For automated testing, see `test-runner.sh` script.

---

## Next Steps

After successful testing:
1. Deploy to staging environment
2. Run for 24-48 hours with monitoring
3. Verify no memory/resource leaks
4. Deploy to production
5. Set up alerting on NOTIFICATION.QUEUE

---

## Support

For issues or questions:
- Review `CHANGELOG.md` for recent changes
- Check `CLAUDE.md` for architecture details
- Review `README.md` for business rules
- Check `BUGFIXES.md` for known issues