# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Project Rules (C#)

## Minimal-diff policy
- If a function already exists, **preserve its implementation and signature** unless:
  1) there’s a correctness bug, 2) compile/test failure proves a change is required, or
  3) I explicitly ask for a refactor.
- Prefer **minimal diffs**: change the fewest lines needed; avoid churn (renames, reshuffles, stylistic rewrites).
- Do not change public APIs without explicit instruction.
- Keep formatting consistent with current files (`dotnet format` acceptable, but no wholesale rewrites).
- When you must change an existing function, explain *why* and show a concise patch.


## Common Development Commands

### Build and Test
```bash
# Build the solution
dotnet build ContainerApp.Manager.sln -c Release

# Build for Debug
dotnet build ContainerApp.Manager.sln -c Debug

# Restore dependencies
dotnet restore ./src/ContainerApp.Manager/ContainerApp.Manager.csproj
```

### Docker Operations
```bash
# Build Docker image
docker build -t containerapp-manager .

# Run container locally (exposes port 8080)
docker run -p 8080:8080 containerapp-manager
```

### Deployment
The project uses GitHub Actions for CI/CD:
- Triggered on pushes to `main` branch
- Builds with .NET 8.0
- Creates Docker image and pushes to Azure Container Registry
- Deploys infrastructure using Bicep templates in `deploy/main.bicep`

## Architecture Overview

This is an Azure Container Apps management service built with .NET 8.0 Worker Service that monitors and controls container applications based on EMS (Energy Management System) data and scheduled policies.

### Core Components

**Decision Engine (`DecisionEngineService`)**
- Main orchestration service that runs as a background service
- Only runs on the leader instance (uses leader election)
- Evaluates EMS data against monitoring configurations
- Triggers scaling actions based on policies and schedules

**Leader Election (`LeaderElectionService`)**
- Ensures only one instance makes scaling decisions in multi-replica deployments
- Uses Azure Table Storage for coordination

**Container App Manager (`ContainerAppManager`)**
- Interfaces with Azure Resource Manager APIs
- Manages Azure Container Apps scaling operations
- Handles authentication via Azure Identity

**EMS Integration (`EmsClient`)**
- Fetches energy/usage data from external EMS systems
- Data is used to make scaling decisions

**State Management (`TableStateStore`)**
- Persists application state using Azure Table Storage
- Tracks scaling decisions and operational history

**Scheduling (`ScheduleEvaluator`, `SchedulerService`)**
- Evaluates cron-based schedules for automated scaling
- Uses Quartz.NET for job scheduling
- Supports complex time-based policies

**Notifications (`NotificationService`)**
- Sends alerts and updates via Azure Communication Services
- Email notifications for scaling events and system status

### Configuration Structure

**Monitor Options (`MonitorOptions`)**
- Defines monitoring mappings and scaling policies
- Configured via `appsettings.json` under the `Monitor` section
- Must have at least one mapping configured

**Azure Services Integration**
- Storage: Azure Blob Storage and Table Storage (via connection string or Managed Identity)
- Communication: Azure Communication Services for email
- Key Vault: Optional configuration source for secrets
- Application Insights: Telemetry and monitoring via OpenTelemetry

### Health Endpoints
- `/health/live`: Always returns 200 (liveness probe)
- `/health/ready`: Returns 200 only if current instance is the leader (readiness probe)

### Key Technologies
- .NET 8.0 Worker Service with ASP.NET Core minimal APIs
- Azure Resource Manager SDK for Container Apps management
- Quartz.NET for job scheduling
- OpenTelemetry with Azure Monitor for observability
- Polly for resilience patterns
- Azure Identity for authentication