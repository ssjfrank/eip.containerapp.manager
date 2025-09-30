# Testing Quick Start

Get started testing ContainerManager.Service in 5 minutes.

## Prerequisites

- [x] Docker Desktop running
- [x] Docker network `aimco-shared` exists
- [x] EMS server accessible
- [x] Azure Container Apps deployed
- [x] Credentials ready

## Quick Setup

### Option A: Docker Compose (Recommended)

**Fastest way to get started!**

```bash
# 1. Run setup script (creates .env, builds image, starts service)
./setup-test-env.sh
```

The script will:
- Create `.env` from `.env.example` if needed
- Build Docker image
- Ask if you want test mode (debug logging, short timeouts)
- Start services
- Show status and logs

**Manual Docker Compose:**

```bash
# 1. Configure environment
cp .env.example .env
nano .env  # Edit with your values

# 2. Start service (production mode)
docker-compose up -d

# 3. Or start in test mode (debug logging, short timeouts)
docker-compose -f docker-compose.yml -f docker-compose.test.yml up -d

# 4. View logs
docker-compose logs -f

# 5. Stop service
docker-compose down
```

### Option B: Standalone Docker (Alternative)

```bash
# 1. Copy configuration template
cp appsettings.json appsettings.test.json
nano appsettings.test.json  # Edit with your values

# 2. Run automated tests
chmod +x test-runner.sh
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

## Docker Compose Commands

### Common Operations
```bash
# View status
docker-compose ps

# View logs (live)
docker-compose logs -f

# View logs (last 50 lines)
docker-compose logs --tail=50

# Restart service
docker-compose restart

# Rebuild and restart
docker-compose up -d --build

# Stop service
docker-compose down

# Stop and remove volumes
docker-compose down -v
```

### Testing Mode
```bash
# Start with debug logging and short timeouts
docker-compose -f docker-compose.yml -f docker-compose.test.yml up -d

# View test mode logs
docker-compose -f docker-compose.yml -f docker-compose.test.yml logs -f
```

## Troubleshooting

### Container Won't Start
```bash
# Docker Compose: Check logs
docker-compose logs container-manager

# Standalone: Check logs
docker logs cm-test

# Common issues:
# - Invalid configuration (check .env or appsettings.test.json)
# - EMS connection failed (check ServerUrl, credentials)
# - Azure auth failed (check SubscriptionId, credentials)
```

### Network Not Found
```bash
# Check if aimco-shared network exists
docker network ls | grep aimco-shared

# Create network if missing
docker network create aimco-shared
```

### Can't Connect to EMS from Docker
```bash
# If EMS is on localhost, use in .env:
EmsSettings__ServerUrl=tcp://host.docker.internal:7222

# Or check network connectivity
docker-compose exec container-manager ping your-ems-host
```

### Configuration Validation Failed
- Ensure all required fields populated in `.env`
- Check environment variable format (double underscore: `Section__Key`)
- Verify QueueContainerMappings is valid JSON format
- Check .env file syntax (no quotes around values unless needed)

## Full Documentation

- **Docker Compose setup:** `docker-compose.yml`, `docker-compose.test.yml`
- **Environment config:** `.env.example`
- **Setup automation:** `setup-test-env.sh`
- **Test scenarios:** `test-data/test-scenarios.md`
- **Sample data:** `test-data/sample-ems-messages.json`, `test-data/sample-notifications.json`
- **Complete testing guide:** `TEST-GUIDE.md`
- **Standalone test script:** `test-runner.sh`
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