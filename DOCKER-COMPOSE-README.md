# Docker Compose Setup for ContainerManager.Service

Complete Docker Compose environment for testing and deploying ContainerManager.Service.

## Quick Start

```bash
./setup-test-env.sh
```

That's it! The script will guide you through setup and start the service.

---

## Files Created

### 1. **docker-compose.yml**
Main service configuration:
- ContainerManager.Service container
- Health checks every 30 seconds
- Restart policy: unless-stopped
- Uses external network: `aimco-shared`
- Environment variables from `.env` file
- Volume mounts for logs and config

### 2. **docker-compose.test.yml**
Testing overrides:
- Debug logging enabled
- Short timeouts (10s polling, 2min idle)
- Additional test-data volume mount
- Faster health checks (15s interval)

**Usage:** `docker-compose -f docker-compose.yml -f docker-compose.test.yml up`

### 3. **.env.example**
Complete configuration template with all settings:
- Manager settings (polling intervals, timeouts, queue mappings)
- EMS settings (server, credentials, notification queue)
- Azure settings (subscription, resource group, auth)
- Logging settings (log levels)

**Copy to `.env` and fill in your values**

### 4. **test-data/**
Sample data for testing:
- `sample-ems-messages.json` - Example EMS message formats
- `sample-notifications.json` - Expected notification outputs
- `test-scenarios.md` - Detailed testing procedures

### 5. **setup-test-env.sh**
Automated setup script:
- Creates `.env` from template
- Builds Docker image
- Starts services (prod or test mode)
- Shows status and logs

### 6. **.gitignore**
Updated to exclude:
- `.env` file (contains secrets)
- `.env.local` and `.env.*.local`

---

## Usage

### Production Mode

```bash
# Start service
docker-compose up -d

# View logs
docker-compose logs -f

# Check status
docker-compose ps

# Stop service
docker-compose down
```

### Test Mode (Debug Logging + Short Timeouts)

```bash
# Start in test mode
docker-compose -f docker-compose.yml -f docker-compose.test.yml up -d

# View logs
docker-compose logs -f

# Stop
docker-compose down
```

### Common Commands

```bash
# Rebuild and restart
docker-compose up -d --build

# Restart service
docker-compose restart

# View last 50 log lines
docker-compose logs --tail=50

# Check container health
docker-compose ps | grep healthy

# Execute command in container
docker-compose exec container-manager bash
```

---

## Configuration

### Environment Variables (.env)

Create `.env` file from `.env.example`:

```bash
cp .env.example .env
nano .env  # Edit with your values
```

**Required variables:**
```env
# EMS Settings
EmsSettings__ServerUrl=tcp://your-ems-host:7222
EmsSettings__Username=admin
EmsSettings__Password=your-password
EmsSettings__NotificationQueueName=NOTIFICATION.QUEUE

# Azure Settings
AzureSettings__SubscriptionId=your-subscription-id
AzureSettings__ResourceGroupName=your-resource-group
AzureSettings__UseManagedIdentity=false
AzureSettings__TenantId=your-tenant-id
AzureSettings__ClientId=your-client-id
AzureSettings__ClientSecret=your-client-secret

# Manager Settings
ManagerSettings__QueueContainerMappings={"container-app-1": ["queue.1"]}
```

---

## Network Requirements

The service uses external network `aimco-shared`.

**Create network if it doesn't exist:**
```bash
docker network create aimco-shared
```

**Verify network exists:**
```bash
docker network ls | grep aimco-shared
```

---

## Health Checks

The service includes automatic health checks:
- **Production mode:** Every 30 seconds
- **Test mode:** Every 15 seconds
- **Timeout:** 10 seconds (prod), 5 seconds (test)
- **Retries:** 3 attempts
- **Start period:** 40 seconds (prod), 20 seconds (test)

**Check health status:**
```bash
docker-compose ps
# Look for "healthy" status
```

---

## Testing

See `test-data/test-scenarios.md` for detailed testing procedures including:
1. Basic connectivity test
2. Container restart scenario
3. Container stop scenario
4. Multi-queue testing
5. EMS disconnection recovery
6. Notification resilience
7. Graceful shutdown

**Quick test:**
```bash
# Run setup script and choose test mode
./setup-test-env.sh

# Follow logs
docker-compose logs -f

# Trigger test scenarios per test-scenarios.md
```

---

## Troubleshooting

### Service won't start

```bash
# Check logs
docker-compose logs container-manager

# Validate configuration
docker-compose config

# Check .env file
cat .env
```

### Network not found

```bash
# Create the network
docker network create aimco-shared

# Verify
docker network ls
```

### Can't connect to EMS

```bash
# If EMS on localhost, use:
EmsSettings__ServerUrl=tcp://host.docker.internal:7222

# Test connectivity from container
docker-compose exec container-manager ping your-ems-host
```

### Configuration validation failed

- Ensure all required variables in `.env`
- Check format: `Section__Key=value`
- No spaces around `=`
- QueueContainerMappings must be valid JSON
- No quotes around values unless JSON string

---

## Production Deployment

### Recommended Configuration

```env
# Production settings
ManagerSettings__PollingIntervalSeconds=30
ManagerSettings__IdleTimeoutMinutes=10
Serilog__MinimumLevel__Default=Information
AzureSettings__UseManagedIdentity=true  # If running in Azure
```

### Deploy Steps

1. Copy `.env.example` to `.env`
2. Fill in production credentials
3. Review `docker-compose.yml` settings
4. Start service: `docker-compose up -d`
5. Monitor logs: `docker-compose logs -f`
6. Set up external monitoring for NOTIFICATION.QUEUE

---

## Files Reference

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Main service configuration |
| `docker-compose.test.yml` | Testing overrides |
| `.env.example` | Configuration template |
| `.env` | Actual configuration (gitignored) |
| `setup-test-env.sh` | Automated setup script |
| `test-data/` | Sample data and test scenarios |
| `TESTING-QUICKSTART.md` | Quick start guide |
| `TEST-GUIDE.md` | Comprehensive testing guide |

---

## Support

For detailed documentation:
- **Quick start:** `TESTING-QUICKSTART.md`
- **Full testing guide:** `TEST-GUIDE.md`
- **Test scenarios:** `test-data/test-scenarios.md`
- **Architecture:** `CLAUDE.md`
- **Recent changes:** `CHANGELOG.md`