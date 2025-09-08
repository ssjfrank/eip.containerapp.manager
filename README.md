# 🚀 ContainerApp Manager

**Intelligent Azure Container Apps Auto-Scaler with TIBCO EMS Integration**

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![TIBCO EMS](https://img.shields.io/badge/TIBCO%20EMS-10.4.0-orange.svg)](https://www.tibco.com/products/tibco-enterprise-message-service)
[![Azure](https://img.shields.io/badge/Azure-Container%20Apps-0078d4.svg)](https://azure.microsoft.com/en-us/services/container-apps/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ed.svg)](https://www.docker.com/)

## 📋 Overview

ContainerApp Manager is a production-ready, intelligent auto-scaling solution for Azure Container Apps that monitors **TIBCO Enterprise Message Service (EMS)** queues and automatically scales your applications based on real-time message queue metrics, scheduled policies, and idle timeouts.

Perfect for enterprise environments where workloads need dynamic scaling based on message processing demands from TIBCO EMS systems.

## ✨ Key Features

### 🎯 **Simplified Queue-Driven Scaling**
- **Message-Based Scaling** - Simple logic: Messages present = Start, No messages = Stop
- **Enhanced Consumer Detection** - Precise consumer counts via TIBCO EMS Admin API
- **Schedule Window Protection** - Apps stay running during scheduled periods regardless of queue state
- **Multi-Queue Protection** - Prevents restart conflicts across multiple queues per container app
- **Smart Restart Logic** - 3-attempt retry mechanism with exponential backoff

### ⏰ **Schedule-Based Scaling with Protection**
- **Cron Expression Support** - Complex time-based scaling policies using cron syntax
- **Schedule Window Protection** - Never stop apps during their scheduled periods
- **Time Window Management** - Define scaling windows with specific durations
- **Multiple Schedule Support** - Configure multiple overlapping or sequential schedules
- **Timezone Handling** - UTC-based scheduling with proper timezone support

### 🔧 **Enterprise Integration**
- **TIBCO EMS 10.4.0** - Native integration with latest TIBCO Enterprise Message Service
- **TIBCO EMS Admin API** - Enhanced monitoring with precise consumer counts and queue statistics
- **Azure Container Apps** - Direct integration with Azure Resource Manager APIs
- **Azure Services** - Comprehensive Azure ecosystem integration:
  - **Azure Table Storage** - State persistence and leader election
  - **Azure Blob Storage** - Leader election coordination
  - **Azure Key Vault** - Secure configuration management
  - **Azure Communication Services** - Email notifications
  - **Application Insights** - Full observability and monitoring

### 🏗️ **Robust Architecture**
- **Leader Election** - Multi-replica safe with automatic failover
- **Connection Management** - Automatic EMS connection handling with retry policies
- **Error Resilience** - Comprehensive error handling and recovery
- **State Persistence** - Maintains scaling history and operational state
- **Thread Safety** - Concurrent operation support with proper locking

### 📊 **Advanced Monitoring & Alerting**
- **Long Processing Alerts** - Graduated email notifications for messages taking too long (20min, 25min, 30min+)
- **Enhanced Queue Statistics** - Precise message counts, consumer tracking, and throughput metrics
- **Processing Duration Tracking** - Monitor message age and processing times per queue
- **OpenTelemetry Integration** - Distributed tracing and metrics
- **Azure Monitor Export** - Native Azure Application Insights integration
- **Structured Logging** - Comprehensive logging with correlation IDs
- **Health Endpoints** - Kubernetes-ready liveness and readiness probes
- **Rich Email Notifications** - Context-aware alerts with restart attempt tracking

### 🚀 **DevOps Ready**
- **Docker Containerization** - Production-ready container images with manual DLL support
- **Complete Debug Environment** - Docker Compose with Azurite, TIBCO EMS, and mock services
- **Team Development Ready** - Optimized .gitignore, VS Code settings, and 5-minute onboarding
- **GitHub Actions CI/CD** - Automated build, test, and deployment pipeline
- **Azure Bicep IaC** - Infrastructure as Code deployment templates
- **Multi-Environment Support** - Development, staging, and production configurations

## 🏛️ Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   TIBCO EMS     │    │ ContainerApp    │    │ Azure Container │
│   Message       │◄───┤    Manager      │───►│     Apps        │
│   Queues        │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │ Azure Services  │
                    │                 │
                    │ • Table Storage │
                    │ • Blob Storage  │
                    │ • Key Vault     │
                    │ • Communication │
                    │ • App Insights  │
                    └─────────────────┘
```

### Core Components

| Component | Description |
|-----------|-------------|
| **Decision Engine** | Simplified orchestrator using message-driven scaling with schedule protection |
| **EMS Client** | TIBCO EMS integration with Admin API support for precise consumer monitoring |
| **Container App Manager** | Azure Resource Manager integration for scaling operations |
| **Leader Election Service** | Ensures only one instance makes decisions in multi-replica deployments |
| **Enhanced State Store** | Persistent storage with processing duration tracking and restart history |
| **Schedule Evaluator** | Cron-based scheduling with window protection against queue-driven stops |
| **Alert Service** | Long processing alerts and rich email notifications with context |

## 🆕 What's New in v2.0

### 🎯 **Simplified Queue-Driven Scaling**
Gone are complex thresholds and idle timeouts! The new logic is simple and reliable:
- **Messages present in queue → Start container app**
- **No messages in queue → Stop container app**
- **Schedule window active → Keep running regardless of queue state**

### ⏰ **Long Message Processing Alerts**
Monitor message processing times without automatic intervention:
- **Graduated alerts** at configurable intervals (20min, 25min, 30min+)
- **Rich email notifications** with queue status and processing duration
- **Support team decides** - no automatic actions, just intelligent monitoring

### 🔧 **TIBCO EMS Admin API Integration**
Enhanced queue monitoring with precise data:
- **Exact consumer counts** - no more guessing about active receivers
- **Real message statistics** - accurate pending counts and throughput metrics
- **Message age tracking** - know exactly how long messages have been waiting
- **Graceful fallback** - automatically falls back to basic mode if admin API unavailable

### 🐳 **Complete Debug Environment**
Full containerized development experience:
- **Docker Compose setup** with all dependencies included
- **Azurite** for Azure Storage emulation
- **TIBCO EMS server** with pre-configured queues
- **Mock Azure services** for complete local development

## 📦 Installation & Setup

> 🚀 **Quick Start**: New to the project? See [GETTING-STARTED.md](GETTING-STARTED.md) for 5-minute setup guide.

### Prerequisites
- **.NET 8.0 SDK** or later
- **Docker** (for containerization)
- **Azure Subscription** with Container Apps enabled
- **TIBCO EMS Server** with accessible queues
- **Azure Resources:**
  - Storage Account (Table + Blob)
  - Container Apps Environment
  - Azure Communication Services (optional, for notifications)
  - Key Vault (optional, for secure configuration)

### 🔧 Configuration

#### 1. Application Settings (`appsettings.json`)

```json
{
  "Ems": {
    "ConnectionString": "tcp://your-ems-server:7222",
    "Username": "your-ems-username",
    "Password": "your-ems-password",
    "ConnectionTimeoutMs": 30000,
    "ReconnectDelayMs": 5000,
    "MaxReconnectAttempts": 3,
    "AdminUsername": "admin-user",
    "AdminPassword": "admin-password",
    "UseAdminAPI": true,
    "AdminConnectionTimeoutMs": 15000,
    "FallbackToBasicMode": true
  },
  "Storage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "Monitor": {
    "PollIntervalSeconds": 15,
    "CooldownMinutes": 5,
    "MessageProcessingAlerts": {
      "FirstAlertMinutes": 20,
      "FollowupIntervalMinutes": 5,
      "MaxAlerts": 6,
      "AlertEmails": ["support@company.com"],
      "Enabled": true
    },
    "Mappings": [
      {
        "ResourceGroup": "rg-production",
        "ContainerApp": "my-worker-app",
        "DesiredReplicas": 3,
        "Queues": [
          "Q.Orders.Processing",
          "Q.Inventory.Updates"
        ],
        "Schedules": [
          {
            "Cron": "0 0 9 * * MON-FRI",
            "DesiredReplicas": 5,
            "WindowLabel": "Business Hours Start",
            "DurationMinutes": 480
          }
        ],
        "NotifyEmails": [
          "ops-team@company.com",
          "dev-team@company.com"
        ],
        "MaxRestartAttempts": 3,
        "RestartCooldownMinutes": 5,
        "ConsumerTimeoutMinutes": 10,
        "StartupGracePeriodMinutes": 3
      }
    ]
  }
}
```

#### 2. Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `EMS_CONNECTION_STRING` | TIBCO EMS server connection string | ✅ |
| `EMS_USERNAME` | EMS authentication username | ✅ |
| `EMS_PASSWORD` | EMS authentication password | ✅ |
| `EMS_ADMIN_USERNAME` | EMS admin user for enhanced monitoring | ❌ |
| `EMS_ADMIN_PASSWORD` | EMS admin password for enhanced monitoring | ❌ |
| `STORAGE_CONNECTION_STRING` | Azure Storage connection string | ✅ |
| `ACS_CONNECTION_STRING` | Azure Communication Services connection | ❌ |
| `KEYVAULT_URI` | Azure Key Vault URI for secrets | ❌ |

### 🐳 Docker Deployment

#### Build Image
```bash
docker build -t containerapp-manager:latest .
```

#### Run Container
```bash
docker run -d \
  --name containerapp-manager \
  -p 8080:8080 \
  -e EMS_CONNECTION_STRING="tcp://ems-server:7222" \
  -e EMS_USERNAME="your-username" \
  -e EMS_PASSWORD="your-password" \
  -e STORAGE_CONNECTION_STRING="your-storage-connection" \
  containerapp-manager:latest
```

### ☁️ Azure Deployment

#### 1. Infrastructure Setup (Bicep)
```bash
az deployment group create \
  --resource-group rg-containerapp-manager \
  --template-file deploy/main.bicep \
  --parameters \
    environmentName=containerapp-env \
    containerAppName=containerapp-manager \
    image=your-registry/containerapp-manager:latest
```

#### 2. GitHub Actions Deployment
Set up the following secrets in your GitHub repository:
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `ACR_LOGIN_SERVER`

The CI/CD pipeline will automatically deploy on pushes to `main` branch.

### 🐳 Debug Environment Setup

For local development and testing, use the complete Docker debug environment:

```bash
# Start all debug services (Azurite, TIBCO EMS, mock Azure services)
docker-compose -f docker-compose.debug.yml up -d

# View logs
docker-compose -f docker-compose.debug.yml logs -f

# Stop all services
docker-compose -f docker-compose.debug.yml down
```

**Debug Services Included:**
- **Azurite** - Azure Storage emulation (Tables, Blobs, Queues)
- **TIBCO EMS** - Pre-configured with test queues
- **Azure Mock Services** - Communication services and other Azure API mocks
- **Pre-configured Queues:** `Q.Debug.Test1`, `Q.Debug.Test2`, `Q.Debug.Worker`, `Q.Debug.MultiQueue1-3`

See [DEBUG-SETUP.md](DEBUG-SETUP.md) for detailed setup instructions.

### 🔧 TIBCO EMS Admin API Integration

For enhanced queue monitoring with precise consumer counts and message statistics:

#### Required Files
Place these DLLs from your TIBCO EMS installation in `src/ContainerApp.Manager/libs/tibco/`:
- `TIBCO.EMS.ADMIN.dll`
- `TIBCO.EMS.UFO.dll`

#### Enhanced Capabilities
- **Exact Consumer Counts** - Know precisely how many active consumers are processing
- **Detailed Message Statistics** - Access message rates, throughput, and queue performance
- **Message Age Information** - Determine processing duration and oldest message age
- **Real-time Queue Health** - Monitor queue performance and health indicators

#### Configuration
```json
{
  "Ems": {
    "UseAdminAPI": true,
    "AdminUsername": "admin-user",
    "AdminPassword": "admin-password", 
    "AdminConnectionTimeoutMs": 15000,
    "FallbackToBasicMode": true
  }
}
```

See [TIBCO-ADMIN-SETUP.md](TIBCO-ADMIN-SETUP.md) for complete setup instructions.

## 🎛️ Usage Examples

### Simple Message-Driven Scaling
```json
{
  "ResourceGroup": "rg-production",
  "ContainerApp": "order-processor",
  "DesiredReplicas": 2,
  "Queues": ["Q.Orders.New", "Q.Orders.Priority"],
  "NotifyEmails": ["ops@company.com"],
  "MaxRestartAttempts": 3,
  "RestartCooldownMinutes": 5
}
```
**Logic**: Messages present → Start, No messages → Stop

### Schedule-Protected Scaling
```json
{
  "ResourceGroup": "rg-production", 
  "ContainerApp": "business-processor",
  "DesiredReplicas": 3,
  "Queues": ["Q.Business.Critical"],
  "Schedules": [
    {
      "Cron": "0 0 8 * * MON-FRI",
      "DesiredReplicas": 5,
      "WindowLabel": "Business Hours",
      "DurationMinutes": 480
    }
  ],
  "NotifyEmails": ["business-ops@company.com"]
}
```
**Logic**: During business hours → Keep running regardless of queue state

### Long Processing Alerts
```json
{
  "MessageProcessingAlerts": {
    "FirstAlertMinutes": 20,
    "FollowupIntervalMinutes": 5,
    "MaxAlerts": 6,
    "AlertEmails": ["support@company.com", "dev-team@company.com"],
    "Enabled": true
  }
}
```
**Alerts**: 20min → First alert, 25min → Second alert, 30min+ → Final alerts

### Multi-Queue Protection
```json
{
  "Queues": [
    "Q.Processing.High",
    "Q.Processing.Medium", 
    "Q.Processing.Low"
  ],
  "DesiredReplicas": 4,
  "ConsumerTimeoutMinutes": 10,
  "StartupGracePeriodMinutes": 3
}
```
**Logic**: Any queue has messages → Start, All queues empty → Stop

## 📊 Monitoring & Health Checks

### Health Endpoints
- `GET /health/live` - Liveness probe (always returns 200)
- `GET /health/ready` - Readiness probe (200 if leader, 503 if follower)

### Observability
- **Structured Logging** with correlation IDs
- **OpenTelemetry Tracing** for distributed operations
- **Custom Metrics** for scaling decisions and EMS connectivity
- **Azure Application Insights** integration

### Email Notifications
Automatic notifications for:
- Scaling events (start/stop/restart)
- Connection failures
- Configuration errors
- Leader election changes

## 🔒 Security Features

- **Azure Managed Identity** support
- **Key Vault integration** for sensitive configuration
- **Connection string masking** in logs
- **Secure credential handling** for EMS authentication
- **RBAC integration** with Azure Container Apps

## 🛠️ Development

### Build & Test
```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build ContainerApp.Manager.sln -c Release

# Run tests (when available)
dotnet test

# Run locally
dotnet run --project src/ContainerApp.Manager
```

### Local Development

#### Docker Debug Environment (Recommended)
```bash
# Start complete debug environment
docker-compose -f docker-compose.debug.yml up -d

# Run the application in debug mode
dotnet run --project src/ContainerApp.Manager --launch-profile Debug
```

See [DEBUG-SETUP.md](DEBUG-SETUP.md) for complete setup instructions.

#### Manual Development Setup
1. Update `appsettings.Development.json` with local settings
2. Use Azure Storage Emulator (Azurite) or real Azure Storage
3. Configure TIBCO EMS test environment
4. **Optional**: Set up TIBCO EMS Admin API (see [TIBCO-ADMIN-SETUP.md](TIBCO-ADMIN-SETUP.md))

#### TIBCO EMS Admin DLLs
For enhanced queue monitoring, place required DLLs in `src/ContainerApp.Manager/libs/tibco/`:
- `TIBCO.EMS.ADMIN.dll`
- `TIBCO.EMS.UFO.dll`

**Note**: These DLLs are not available via NuGet and must be copied from your TIBCO EMS installation.

## 📈 Performance & Scalability

- **Lightweight footprint** - Optimized .NET 8.0 runtime
- **Efficient polling** - Configurable intervals with smart backoff
- **Connection pooling** - Reused EMS connections
- **Leader election** - Supports horizontal scaling
- **Resource optimization** - Minimal CPU and memory usage

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

- **Documentation**: Check the [CLAUDE.md](CLAUDE.md) file for detailed technical guidance
- **Issues**: Report bugs and feature requests via [GitHub Issues](https://github.com/ssjfrank/containerapp.manager/issues)
- **Discussions**: Join the conversation in [GitHub Discussions](https://github.com/ssjfrank/containerapp.manager/discussions)

## 🏷️ Version History

- **v1.0.0** - Initial release with TIBCO EMS 10.4.0 integration
  - Real-time queue monitoring
  - Schedule-based scaling
  - Azure Container Apps integration
  - Leader election support
  - Comprehensive error handling

---

<div align="center">

**Built with ❤️ for Enterprise Azure Container Apps Scaling**

[⭐ Star this repo](https://github.com/ssjfrank/containerapp.manager) | [🐛 Report Bug](https://github.com/ssjfrank/containerapp.manager/issues) | [💡 Request Feature](https://github.com/ssjfrank/containerapp.manager/issues)

</div>