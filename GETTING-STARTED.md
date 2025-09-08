# 🚀 Getting Started - ContainerApp Manager

**Quick setup guide for new team members to get the Azure Container Apps Manager running locally with minimal effort.**

## ✅ Prerequisites

- **.NET 8.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Docker & Docker Compose** - [Install Docker Desktop](https://www.docker.com/products/docker-desktop/)
- **Git** - For cloning the repository

## 🏃‍♂️ Quick Start (5 minutes)

### 1. Clone and Setup
```bash
# Clone the repository
git clone https://github.com/ssjfrank/containerapp.manager.git
cd containerapp.manager

# Restore dependencies
dotnet restore
```

### 2. Configure Development Settings
```bash
# Copy the development template
cp src/ContainerApp.Manager/appsettings.Development.json.template src/ContainerApp.Manager/appsettings.Development.json

# Copy environment variables template
cp .env.example .env
```

### 3. Start Debug Environment (Docker)
```bash
# Start all debug services (Azurite, TIBCO EMS, mock services)
docker-compose -f docker-compose.debug.yml up -d

# Wait ~30 seconds for services to be ready
```

### 4. Run the Application
```bash
# Build and run
dotnet build
dotnet run --project src/ContainerApp.Manager
```

🎉 **That's it!** The application should now be running with:
- Health endpoints at `http://localhost:8080/health/live` and `http://localhost:8080/health/ready`
- Connected to local TIBCO EMS and Azurite storage
- Debug logging enabled

## 📋 What You Get Out of the Box

### Debug Services (Docker Compose)
- **Azurite** - Local Azure Storage emulator
- **TIBCO EMS** - Pre-configured with test queues
- **Mock Azure Services** - Communication services simulation

### Pre-configured Queues
- `Q.Debug.Test1`, `Q.Debug.Test2`
- `Q.Debug.Worker`
- `Q.Debug.MultiQueue1-3`

### Monitoring Configuration
- Simple debug mappings for testing scaling logic
- Fast polling (10 seconds) for quick feedback
- Email alerts configured for development

## 🔧 Configuration Customization

### Update Development Settings
Edit `src/ContainerApp.Manager/appsettings.Development.json`:

```json
{
  "Ems": {
    "ConnectionString": "tcp://localhost:7222",
    "Username": "admin",
    "Password": "admin"
  },
  "Monitor": {
    "Mappings": [
      {
        "ResourceGroup": "rg-dev",
        "ContainerApp": "your-test-app",
        "Queues": ["Q.Your.Queue"]
      }
    ]
  }
}
```

### Environment Variables
Update `.env` file with your actual Azure resources if needed:
```bash
# For real Azure services instead of mocks
STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=...
ACS_CONNECTION_STRING=endpoint=https://your-acs.communication.azure.com/...
```

## 🏗️ Production Setup (Optional)

### TIBCO EMS Admin API (Enhanced Monitoring)
1. Copy TIBCO DLLs to `src/ContainerApp.Manager/libs/tibco/`:
   - `TIBCO.EMS.ADMIN.dll`
   - `TIBCO.EMS.UFO.dll`
2. See [TIBCO-ADMIN-SETUP.md](TIBCO-ADMIN-SETUP.md) for details

### Azure Resources
For production deployment, you'll need:
- Azure Container Apps Environment
- Azure Storage Account (Tables + Blobs)
- Azure Communication Services (for email)
- TIBCO EMS Server accessible from Azure

## 🛠️ Development Commands

```bash
# Build solution
dotnet build ContainerApp.Manager.sln -c Debug

# Run with Debug profile
dotnet run --project src/ContainerApp.Manager --launch-profile Debug

# Format code
dotnet format ContainerApp.Manager.sln

# View debug services logs
docker-compose -f docker-compose.debug.yml logs -f

# Stop debug services
docker-compose -f docker-compose.debug.yml down
```

## 📊 Health Checks

Once running, verify everything works:

```bash
# Liveness probe (should always return 200)
curl http://localhost:8080/health/live

# Readiness probe (returns 200 only if leader)
curl http://localhost:8080/health/ready
```

## ✅ Verify Your Setup

After completing the quick start, verify everything is working:

### 1. Git Status Check
```bash
# Should show clean working tree with no secrets exposed
git status

# Should NOT show appsettings.Development.json or .env
# Should show template files and team VS Code settings
```

### 2. Health Check Verification
```bash
# Liveness probe (should always return 200)
curl http://localhost:8080/health/live

# Readiness probe (returns 200 only if leader)
curl http://localhost:8080/health/ready
```

### 3. VS Code Integration Check
- Open project in VS Code
- Recommended extensions should auto-prompt for installation
- Launch configurations should be available (F5 to debug)
- Build tasks should be available (Ctrl+Shift+P -> "Tasks: Run Task")

## 🔍 Troubleshooting

### Common Issues

**Git Showing Unwanted Files:**
```bash
# Verify gitignore is working - these should be excluded:
ls src/ContainerApp.Manager/bin/    # Should be hidden by git
ls src/ContainerApp.Manager/obj/    # Should be hidden by git
git status                          # Should not show build artifacts
```

**VS Code Settings Not Loading:**
- Ensure you didn't accidentally exclude `.vscode/` folder
- VS Code settings should be shared across team (not user-specific)

**Port Conflicts:**
```bash
# Check what's using ports 7222, 8080, 10000-10002
netstat -tulpn | grep -E '7222|8080|1000[0-2]'
```

**TIBCO EMS Connection:**
- Ensure Docker containers are running: `docker ps`
- Check EMS logs: `docker-compose -f docker-compose.debug.yml logs tibco-ems`

**Storage Connection:**
- Verify Azurite is running: `docker-compose -f docker-compose.debug.yml logs azurite`

**.NET Build Issues:**
```bash
# Clean and restore
dotnet clean
dotnet restore
dotnet build
```

**Configuration Template Issues:**
```bash
# Verify template files exist and are readable
ls -la src/ContainerApp.Manager/appsettings.Development.json.template
ls -la .env.example

# Check if you have actual config files (should exist after setup)
ls -la src/ContainerApp.Manager/appsettings.Development.json
ls -la .env
```

## 🎯 Team Independence Checklist

Use this checklist to verify your team can work independently:

### ✅ **New Team Member Onboarding** (5 minutes)
- [ ] Clone repository with `git clone https://github.com/ssjfrank/containerapp.manager.git`
- [ ] Copy templates: `cp src/ContainerApp.Manager/appsettings.Development.json.template src/ContainerApp.Manager/appsettings.Development.json`
- [ ] Copy env file: `cp .env.example .env`  
- [ ] Start services: `docker-compose -f docker-compose.debug.yml up -d`
- [ ] Build and run: `dotnet build && dotnet run --project src/ContainerApp.Manager`
- [ ] Verify health: `curl http://localhost:8080/health/live` returns 200

### ✅ **Git Repository Health**
- [ ] `git status` shows clean tree (no build artifacts, no secrets)
- [ ] Template files are committed and accessible
- [ ] VS Code settings work consistently across team
- [ ] No accidental commits of user-specific files

### ✅ **Development Environment**
- [ ] VS Code recommended extensions auto-install
- [ ] Debug configurations work (F5 to debug)
- [ ] Build tasks available (Ctrl+Shift+P -> Tasks)
- [ ] Docker debug environment starts without errors
- [ ] All health endpoints respond correctly

### ✅ **Configuration Management**
- [ ] Template files provide clear examples
- [ ] Environment variables properly documented
- [ ] No hardcoded secrets or connection strings
- [ ] Local development configs work out of the box

## 📚 Next Steps

1. **Read the Architecture**: Check [CLAUDE.md](CLAUDE.md) for technical details
2. **Debug Setup**: See [DEBUG-SETUP.md](DEBUG-SETUP.md) for advanced debugging
3. **TIBCO Setup**: Review [TIBCO-ADMIN-SETUP.md](TIBCO-ADMIN-SETUP.md) for enhanced monitoring
4. **Full Documentation**: Read [README.md](README.md) for complete feature overview

## 🆘 Need Help?

- **Issues**: Report problems via [GitHub Issues](https://github.com/ssjfrank/containerapp.manager/issues)
- **Discussions**: Ask questions in [GitHub Discussions](https://github.com/ssjfrank/containerapp.manager/discussions)
- **Architecture**: Review technical details in [CLAUDE.md](CLAUDE.md)

---

**Happy coding! 🎯** This setup should get you running in under 5 minutes with a fully functional local development environment.