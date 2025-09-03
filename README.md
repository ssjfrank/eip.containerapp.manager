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

### 🎯 **Smart Auto-Scaling**
- **Queue Depth Monitoring** - Real-time TIBCO EMS queue depth analysis
- **Consumer Presence Detection** - Monitors active message consumers
- **Threshold-Based Scaling** - Configurable start thresholds and desired replica counts
- **Idle Timeout Management** - Automatic scale-down when queues are empty
- **Cooldown Protection** - Prevents scaling thrashing with configurable cooldown periods

### ⏰ **Schedule-Based Scaling**
- **Cron Expression Support** - Complex time-based scaling policies using cron syntax
- **Time Window Management** - Define scaling windows with specific durations
- **Multiple Schedule Support** - Configure multiple overlapping or sequential schedules
- **Timezone Handling** - UTC-based scheduling with proper timezone support

### 🔧 **Enterprise Integration**
- **TIBCO EMS 10.4.0** - Native integration with latest TIBCO Enterprise Message Service
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

### 📊 **Monitoring & Observability**
- **OpenTelemetry Integration** - Distributed tracing and metrics
- **Azure Monitor Export** - Native Azure Application Insights integration
- **Structured Logging** - Comprehensive logging with correlation IDs
- **Health Endpoints** - Kubernetes-ready liveness and readiness probes
- **Email Notifications** - Configurable alerts for scaling events

### 🚀 **DevOps Ready**
- **Docker Containerization** - Production-ready container images
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
| **Decision Engine** | Main orchestrator that evaluates scaling decisions based on EMS data and schedules |
| **EMS Client** | TIBCO EMS integration with connection management and queue monitoring |
| **Container App Manager** | Azure Resource Manager integration for scaling operations |
| **Leader Election Service** | Ensures only one instance makes decisions in multi-replica deployments |
| **State Store** | Persistent storage for scaling history and operational state |
| **Schedule Evaluator** | Cron-based scheduling engine for time-window policies |
| **Notification Service** | Email alerting system for scaling events and system status |

## 📦 Installation & Setup

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
    "MaxReconnectAttempts": 3
  },
  "Storage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "Monitor": {
    "PollIntervalSeconds": 15,
    "CooldownMinutes": 5,
    "Mappings": [
      {
        "ResourceGroup": "rg-production",
        "ContainerApp": "my-worker-app",
        "DesiredReplicas": 3,
        "StartThreshold": 5,
        "IdleTimeoutMinutes": 10,
        "NoListenerTimeoutMinutes": 3,
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
        ]
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
| `STORAGE_CONNECTION_STRING` | Azure Storage connection string | ✅ |
| `ACS_CONNECTION_STRING` | Azure Communication Services connection | ❌ |
| `KEYVAULT_URI` | Azure Key Vault URI for secrets | ❌ |
| `ACS_EMAIL_SENDER` | Sender email for notifications | ❌ |

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

## 🎛️ Usage Examples

### Basic Queue Monitoring
```json
{
  "ResourceGroup": "rg-production",
  "ContainerApp": "order-processor",
  "DesiredReplicas": 2,
  "StartThreshold": 10,
  "IdleTimeoutMinutes": 15,
  "Queues": ["Q.Orders.New", "Q.Orders.Priority"]
}
```

### Advanced Scheduled Scaling
```json
{
  "Schedules": [
    {
      "Cron": "0 0 8 * * MON-FRI",
      "DesiredReplicas": 5,
      "WindowLabel": "Morning Peak",
      "DurationMinutes": 240
    },
    {
      "Cron": "0 0 13 * * MON-FRI", 
      "DesiredReplicas": 8,
      "WindowLabel": "Afternoon Peak",
      "DurationMinutes": 300
    }
  ]
}
```

### Multi-Queue Monitoring
```json
{
  "Queues": [
    "Q.Processing.High",
    "Q.Processing.Medium", 
    "Q.Processing.Low",
    "Q.DeadLetter.Retry"
  ],
  "StartThreshold": 15,
  "DesiredReplicas": 4
}
```

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
1. Update `appsettings.Development.json` with local settings
2. Use Azure Storage Emulator or real Azure Storage
3. Configure TIBCO EMS test environment

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