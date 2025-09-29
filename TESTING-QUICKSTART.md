# Testing Quick Start

Get started testing ContainerManager.Service in 5 minutes.

## Prerequisites

- [x] Docker Desktop running
- [x] EMS server accessible
- [x] Azure Container Apps deployed
- [x] Credentials ready

## Quick Setup

### 1. Configure Testing Environment

```bash
# Copy configuration template
cp appsettings.json appsettings.test.json
```

Edit `appsettings.test.json` with your actual values:
- EMS ServerUrl, Username, Password
- Azure SubscriptionId, ResourceGroupName, credentials
- QueueContainerMappings (your actual container and queue names)

### 2. Run Automated Tests

```bash
# Make script executable
chmod +x test-runner.sh

# Run all tests
./test-runner.sh all
```

**Expected output:**
```
[INFO] Building Docker image
[SUCCESS] Image built successfully
[INFO] Starting container with test configuration
[SUCCESS] Container is running
[SUCCESS] Services initialized successfully
[SUCCESS] EMS connection successful
[SUCCESS] Azure client initialized
[SUCCESS] All tests completed!
```

### 3. Interactive Testing

```bash
# Run with live logs
./test-runner.sh interactive
```

Press `Ctrl+C` to stop.

## Test Scenarios

### Scenario 1: Container Restart
1. Stop your container app (scale to 0)
2. Send messages to queue
3. Watch service automatically restart container

### Scenario 2: Container Stop
1. Ensure container running
2. Stop sending messages
3. Wait 10 minutes (or configured IdleTimeoutMinutes)
4. Watch service automatically stop container

### Scenario 3: Check Notifications
Browse `NOTIFICATION.QUEUE` in EMS to see operation notifications.

## Troubleshooting

### Container Won't Start
```bash
# Check logs
docker logs cm-test

# Common issues:
# - Invalid configuration (check appsettings.test.json)
# - EMS connection failed (check ServerUrl, credentials)
# - Azure auth failed (check SubscriptionId, credentials)
```

### Can't Connect to EMS from Docker
```bash
# If EMS is on localhost, use:
"ServerUrl": "tcp://host.docker.internal:7222"

# Or run with host networking:
docker run --network host ...
```

### Configuration Validation Failed
- Ensure all required fields populated
- Check JSON syntax is valid
- Verify at least one QueueContainerMapping exists

## Full Documentation

- **Complete testing guide:** `TEST-GUIDE.md`
- **Test automation script:** `test-runner.sh`
- **Change log:** `CHANGELOG.md`
- **Architecture:** `CLAUDE.md`
- **User guide:** `README.md`

## Next Steps

After successful testing:
1. ✅ All automated tests pass
2. ⬜ Deploy to staging environment
3. ⬜ Run for 24-48 hours
4. ⬜ Deploy to production
5. ⬜ Set up monitoring/alerting

## Support

Issues? Check:
1. `TEST-GUIDE.md` - Troubleshooting section
2. `CHANGELOG.md` - Recent changes
3. Container logs: `docker logs <container-id>`