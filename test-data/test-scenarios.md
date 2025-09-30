# Test Scenarios Guide

This guide provides step-by-step instructions for testing ContainerManager.Service using the sample data.

## Prerequisites

- Docker Compose environment running
- TIBCO EMS server accessible
- Azure Container Apps deployed
- Access to EMS admin tools or console

---

## Scenario 1: Basic Connectivity Test

**Objective:** Verify service can connect to EMS and Azure

**Steps:**
1. Start the service:
   ```bash
   docker-compose up -d
   ```

2. Check logs for successful initialization:
   ```bash
   docker-compose logs -f container-manager
   ```

3. Look for these log messages:
   - `[INF] All services initialized successfully`
   - `[INF] EMS connected to tcp://...`
   - `[INF] Azure Container Apps client initialized`
   - `[INF] DecisionEngine initialized with X container mappings`

**Expected Result:** Service starts without errors and connects to both EMS and Azure

---

## Scenario 2: Container Restart on Messages Without Receivers

**Objective:** Test automatic container restart when messages arrive without receivers

**Setup:**
1. Ensure container app is stopped (scale to 0 in Azure Portal)
2. Check current queue status is empty

**Steps:**
1. Send test messages to the queue using EMS admin tool:
   ```
   # Example using tibjmsadmin
   tibjmsadmin -server tcp://localhost:7222 -user admin -password admin
   > send queue.app1.requests "Test message 1"
   > send queue.app1.requests "Test message 2"
   > send queue.app1.requests "Test message 3"
   > exit
   ```

2. Watch service logs:
   ```bash
   docker-compose logs -f container-manager
   ```

3. Expected log sequence:
   ```
   [DBG] Retrieved X queues, analyzing for actions
   [INF] Queue queue.app1.requests has 3 messages with no receivers → Restart container-app-1
   [INF] Decision for container-app-1: Restart
   [WRN] Starting restart operation for container container-app-1
   [INF] Container app container-app-1 scaled to 0
   [INF] Scaling up container app container-app-1
   [INF] Container app container-app-1 restarted successfully
   [INF] Waiting up to 00:05:00 for receivers on queues
   [INF] All queues have receivers after 00:XX:XX
   [INF] Published notification: Container=container-app-1, Action=RESTART, Status=SUCCESS
   ```

4. Verify in Azure Portal:
   - Container app scaled back up
   - Replica count > 0

5. Check NOTIFICATION.QUEUE for notification message:
   ```json
   {
     "Timestamp": "...",
     "ContainerApp": "container-app-1",
     "Action": "RESTART",
     "Status": "SUCCESS",
     "Message": "Container restarted successfully, receivers detected on queues: queue.app1.requests"
   }
   ```

**Expected Result:**
- Container automatically restarts
- Messages get processed
- SUCCESS notification sent

---

## Scenario 3: Container Stop on Idle Timeout

**Objective:** Test automatic container stop when queues are idle

**Setup:**
1. Ensure container app is running (has receivers)
2. Ensure queues are empty (no messages)
3. Configure short idle timeout for testing (e.g., 2 minutes)

**Steps:**
1. Wait for configured idle timeout period

2. Watch logs for idle tracking:
   ```
   [DBG] Queue queue.app1.requests idle for 00:01:30, waiting for 00:02:00
   [DBG] Queue queue.app1.events idle for 00:01:30, waiting for 00:02:00
   ```

3. After timeout expires, expect:
   ```
   [INF] All queues for container-app-1 idle for 00:02:00 → Stop container
   [INF] Decision for container-app-1: Stop
   [WRN] Starting stop operation for container container-app-1
   [INF] Stopping container app container-app-1
   [INF] Container app container-app-1 stopped successfully due to idle queues
   [INF] Published notification: Container=container-app-1, Action=STOP, Status=SUCCESS
   ```

4. Verify in Azure Portal:
   - Container app scaled to 0
   - No replicas running

**Expected Result:**
- Container automatically stops after idle timeout
- SUCCESS notification sent

---

## Scenario 4: Multi-Queue Container Testing

**Objective:** Test behavior with container monitoring multiple queues

**Setup:**
Configure container with multiple queues in .env:
```
ManagerSettings__QueueContainerMappings={"container-app-1": ["queue.1", "queue.2", "queue.3"]}
```

**Test 4a: Restart Triggered by One Queue**

1. Stop container app
2. Send messages to only queue.1
3. Leave queue.2 and queue.3 empty

**Expected:** Container restarts because ANY queue has messages without receivers

**Test 4b: Container Stays Running with Activity**

1. Ensure container running
2. Send messages to queue.1 (has receivers)
3. Leave queue.2 and queue.3 idle

**Expected:** Container stays running because ANY queue has activity

**Test 4c: Stop Only When All Queues Idle**

1. Ensure container running
2. Stop sending messages to all queues
3. Wait for idle timeout

**Expected:** Container stops only after ALL queues idle for timeout period

---

## Scenario 5: EMS Disconnection Recovery

**Objective:** Test service resilience when EMS disconnects

**Steps:**
1. Start service normally
2. Stop EMS server or block network connection
3. Wait 1-2 minutes
4. Restart EMS server or restore connection

**Expected Logs:**
```
[WRN] EMS connection check failed, attempting reconnect
[WRN] Failed to reconnect to EMS: <error>
...
[INF] EMS connection re-established
[DBG] Retrieved X queues, analyzing for actions
```

**Expected Result:** Service automatically reconnects and resumes operation

---

## Scenario 6: Notification Queue Unavailable

**Objective:** Test service continues when notification queue fails

**Steps:**
1. Delete or disable NOTIFICATION.QUEUE in EMS
2. Trigger a restart or stop operation
3. Recreate NOTIFICATION.QUEUE
4. Trigger another operation

**Expected Behavior:**
```
[ERR] Failed to initialize notification publisher
[WRN] Notification publisher not initialized, attempting to reconnect
[WRN] Notification publisher not initialized and in backoff period, skipping notification
```

After queue recreated:
```
[INF] Notification publisher initialized for queue NOTIFICATION.QUEUE
[INF] Published notification: ...
```

**Expected Result:** Service continues core functionality even when notifications fail

---

## Scenario 7: Graceful Shutdown

**Objective:** Test clean shutdown during operations

**Steps:**
1. Start a restart operation
2. Immediately stop the service:
   ```bash
   docker-compose down
   ```

**Expected Logs:**
```
[INF] Waiting for X background operations to complete
[INF] All background operations completed
[DBG] Disposing ContainerManager resources
[INF] Notification publisher closed
[INF] ContainerManager MonitoringWorker stopping
```

**Expected Result:** Service waits for operations and shuts down cleanly

---

## Monitoring Tools

### Docker Compose Logs
```bash
# Follow all logs
docker-compose logs -f

# Filter by service
docker-compose logs -f container-manager

# Last 100 lines
docker-compose logs --tail=100

# Since timestamp
docker-compose logs --since 2025-09-29T10:00:00
```

### Container Status
```bash
# Check running containers
docker-compose ps

# Check container health
docker-compose ps | grep healthy
```

### EMS Queue Monitoring
```bash
# Using tibjmsadmin
tibjmsadmin -server tcp://localhost:7222 -user admin -password admin
> show queues
> show queue queue.app1.requests
> browse NOTIFICATION.QUEUE
```

### Azure Container Apps
```bash
# Check replica count
az containerapp show \
  --name container-app-1 \
  --resource-group your-rg \
  --query "properties.template.scale"

# Check container logs
az containerapp logs show \
  --name container-app-1 \
  --resource-group your-rg \
  --tail 50
```

---

## Troubleshooting

### Service Won't Start
```bash
# Check configuration
cat .env

# Validate Docker Compose
docker-compose config

# Check network exists
docker network ls | grep aimco-shared

# View detailed logs
docker-compose logs container-manager
```

### Can't Connect to EMS
```bash
# Test connectivity from host
telnet your-ems-host 7222

# Test from container
docker-compose exec container-manager nc -zv your-ems-host 7222
```

### Azure Authentication Fails
```bash
# Test service principal
az login --service-principal \
  -u $AzureSettings__ClientId \
  -p $AzureSettings__ClientSecret \
  --tenant $AzureSettings__TenantId

# List container apps
az containerapp list --resource-group your-rg
```

---

## Success Criteria

Before considering testing complete:

- [ ] Service starts successfully with Docker Compose
- [ ] EMS connection works
- [ ] Azure authentication succeeds
- [ ] Container restart scenario works
- [ ] Container stop scenario works
- [ ] Multi-queue logic verified
- [ ] Notifications published successfully
- [ ] Service recovers from EMS disconnection
- [ ] Graceful shutdown works
- [ ] No memory leaks after extended run (1+ hours)

---

## Reference Files

- **sample-ems-messages.json**: Example message formats
- **sample-notifications.json**: Expected notification outputs
- **docker-compose.yml**: Service configuration
- **.env.example**: Configuration template
- **TESTING-QUICKSTART.md**: Quick start guide