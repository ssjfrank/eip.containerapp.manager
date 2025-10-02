# Repository Guidelines

## Project Structure & Module Organization
- `Program.cs` bootstraps the worker host, wires Serilog, and registers services; treat it as the sole entry point.
- `Configuration/` exposes validated option classes; add new settings here and protect them with data annotations.
- `Services/` contains the queue monitor, decision engine, container manager, and notification publisher; keep them stateless singletons.
- `Workers/MonitoringWorker.cs` drives the polling cadence—tune scheduling here instead of adding additional timers.
- `Models/` holds shared DTOs, `Health/` surfaces the optional `ContainerHealthCheck`, and `Libs/` packages the TIBCO EMS binaries required at runtime.

## Build, Test, and Development Commands
- `dotnet restore`
- `dotnet build ContainerManager.Service.csproj`
- `dotnet run --project ContainerManager.Service.csproj`
- `dotnet watch run --project ContainerManager.Service.csproj`

## Coding Style & Naming Conventions
- Use 4-space indentation, PascalCase for types/methods, camelCase for locals, and `_camelCase` only for private readonly fields.
- Prefix interfaces with `I`, prefer dependency injection over static helpers, and rely on `var` when the inferred type is obvious.
- Run `dotnet format` before committing to keep spacing, analyzers, and ordering consistent.

## Testing Guidelines
- Add tests in a sibling `ContainerManager.Service.Tests/` xUnit project; mirror namespaces from the source files.
- Name files `<TypeName>Tests.cs` and methods `Method_State_Expected`; isolate business rules with mocks for `IEmsQueueMonitor`, `IContainerManager`, and `INotificationPublisher`.
- Execute `dotnet test` (optionally `--collect:"XPlat Code Coverage"`) before raising a PR and note relevant results in the description.

## Commit & Pull Request Guidelines
- Follow Conventional Commit prefixes (`feat:`, `fix:`, `docs:`, `refactor:`) as established in the Git history.
- Keep commits scoped and runnable, bundling configuration updates with the behavior they affect.
- PRs should outline the scenario, list impacted queues or Azure resources, link issues, and attach log snippets or screenshots when behavior or observability changes.

## Configuration & Secrets Practices
- Store secrets with `dotnet user-secrets set` (the project already defines a `UserSecretsId`) and sync non-sensitive defaults through `appsettings.json`.
- Document new configuration defaults alongside their validation attributes so misconfiguration fails fast at startup.
- Update both EMS DLLs in `Libs/` together when brokers change to prevent version mismatches.
