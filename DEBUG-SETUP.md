# Docker Debug Setup for Container App Manager

## Overview

This setup allows you to debug the Container App Manager entirely within Docker containers, without installing dependencies on your host machine.

## Prerequisites

- Docker and Docker Compose
- No other dependencies required on host

## Quick Start

```bash
# Start all services for debugging
docker-compose --env-file .env.debug -f docker-compose.debug.yml up --build

# Or run in detached mode
docker-compose --env-file .env.debug -f docker-compose.debug.yml up --build -d

# View logs
docker-compose -f docker-compose.debug.yml logs -f containerapp-manager

# Stop all services
docker-compose -f docker-compose.debug.yml down
```

## Services Included

### 1. **containerapp-manager** (Main Application)
- **Port**: 8080 (main app), 5000 (debug port)
- **Features**: Hot reload with `dotnet watch`
- **Health Check**: `/health/live` and `/health/ready`
- **Environment**: Development with Debug logging

### 2. **azurite** (Azure Storage Emulator)
- **Ports**: 10000 (Blob), 10001 (Queue), 10002 (Table)
- **Purpose**: Emulates Azure Storage for state persistence
- **Connection**: Pre-configured in application settings

### 3. **tibco-ems** (TIBCO EMS Server)
- **Ports**: 7222 (EMS), 7243 (Admin)
- **Credentials**: admin/admin
- **Queues**: Pre-configured with debug queues
- **Purpose**: Message queue system for monitoring

### 4. **azure-mock** (Optional Azure API Mock)
- **Port**: 1080
- **Purpose**: Mock Azure Resource Manager APIs
- **Features**: Container Apps scaling operations

## Configuration Files

### Primary Files
- `docker-compose.debug.yml` - Main Docker Compose configuration
- `.env.debug` - Environment variables for debug mode
- `appsettings.Debug.json` - Application configuration overrides

### Mock Service Configurations  
- `debug/ems-config/ems-init.json` - TIBCO EMS queue setup
- `debug/azure-mock/azure-mock-expectations.json` - Azure API mock responses
- `debug/azure-mock/mockserver.properties` - Mock server settings

## Debugging Features

### Hot Reload
The application supports hot reload via `dotnet watch`. Changes to `.cs` files will automatically rebuild and restart the application.

### Logging
- **Level**: Debug for application, Info for dependencies
- **Output**: Docker container logs
- **View**: `docker-compose -f docker-compose.debug.yml logs -f containerapp-manager`

### Health Checks
- **Liveness**: `curl http://localhost:8080/health/live`
- **Readiness**: `curl http://localhost:8080/health/ready`

## Testing Queue Operations

### Add Messages to Test Queues
```bash
# Access EMS Admin (if available)
# Navigate to http://localhost:7243

# Or use TIBCO EMS client tools to add messages to:
# - Q.Debug.Test1
# - Q.Debug.Test2  
# - Q.Debug.Worker
```

### View Application Behavior
The application will:
1. Connect to TIBCO EMS on startup
2. Poll queues every 10 seconds (configurable)
3. Make scaling decisions based on queue depth
4. Store state in Azurite Table Storage
5. Log all activities to console

## Troubleshooting

### Common Issues

**Application won't start:**
```bash
# Check all services are running
docker-compose -f docker-compose.debug.yml ps

# Check specific service logs
docker-compose -f docker-compose.debug.yml logs azurite
docker-compose -f docker-compose.debug.yml logs tibco-ems
```

**EMS Connection Failed:**
```bash
# Verify EMS container is healthy
docker-compose -f docker-compose.debug.yml exec tibco-ems netstat -an | grep 7222

# Check EMS logs
docker-compose -f docker-compose.debug.yml logs tibco-ems
```

**Storage Connection Issues:**
```bash
# Verify Azurite is accessible
curl http://localhost:10000/devstoreaccount1

# Check Azurite logs
docker-compose -f docker-compose.debug.yml logs azurite
```

### Useful Commands

```bash
# Rebuild only the main application
docker-compose -f docker-compose.debug.yml build containerapp-manager

# Reset all data volumes
docker-compose -f docker-compose.debug.yml down -v

# Connect to application container
docker-compose -f docker-compose.debug.yml exec containerapp-manager bash

# View all container logs
docker-compose -f docker-compose.debug.yml logs
```

## Development Workflow

1. **Start Environment**: `docker-compose --env-file .env.debug -f docker-compose.debug.yml up -d`
2. **Monitor Logs**: `docker-compose -f docker-compose.debug.yml logs -f containerapp-manager`
3. **Make Code Changes**: Files are watched for changes and auto-reload
4. **Test Features**: Add messages to queues, check scaling behavior
5. **Debug Issues**: Use container logs and health endpoints
6. **Clean Up**: `docker-compose -f docker-compose.debug.yml down`

## Configuration Customization

To modify behavior, edit:
- `.env.debug` - Environment variables
- `appsettings.Debug.json` - Application settings
- `debug/ems-config/ems-init.json` - Queue configurations
- `debug/azure-mock/azure-mock-expectations.json` - Mock API responses

Changes to environment files require container restart. Changes to application code trigger automatic reload.