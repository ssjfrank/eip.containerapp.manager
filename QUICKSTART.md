# ContainerManager.Service - Quick Start Guide

## What It Does

Automatically manages Azure Container Apps based on TIBCO EMS queue activity:
- **Restarts** containers when messages pile up without receivers
- **Stops** containers when idle for too long
- **Notifies** via EMS queue for all actions

## Quick Configuration

### 1. Update appsettings.json

```json
{
  "ManagerSettings": {
    "IdleTimeoutMinutes": 10,
    "QueueContainerMappings": {
      "your-container-app-name": ["queue.name.1", "queue.name.2"]
    }
  },
  "EmsSettings": {
    "ServerUrl": "tcp://your-ems-server:7222",
    "Username": "your-username",
    "Password": "your-password"
  },
  "AzureSettings": {
    "SubscriptionId": "your-azure-subscription-id",
    "ResourceGroupName": "your-resource-group"
  }
}
```

### 2. Run Locally

```bash
cd Sources/ContainerManager.Service
dotnet run
```

### 3. Monitor Logs

Logs appear in:
- Console (real-time)
- `logs/container-manager-YYYYMMDD.txt` (file)

## Business Logic Summary

### When Does It Restart?
- Queue has messages (PendingMessageCount > 0)
- Queue has no receivers (ReceiverCount = 0)
- Action: Restart container, wait 5 mins for receivers

### When Does It Stop?
- Queue has no messages for 10 minutes (configurable)
- Queue has receivers (ReceiverCount > 0)
- Action: Stop container (scale to 0)

### Multi-Queue Containers
- Restart: If ANY queue has messages without receivers
- Stop: Only if ALL queues idle + have receivers
- Skip: If ANY queue working normally (messages + receivers)

## Notification Queue

All actions publish to `NOTIFICATION.QUEUE`:

```json
{
  "ContainerApp": "container-app-1",
  "Action": "RESTART",
  "Status": "SUCCESS",
  "Message": "Container restarted successfully"
}
```

Status values:
- **SUCCESS**: Operation completed
- **WARNING**: Completed with issues
- **FAILURE**: Operation failed

## Troubleshooting

### Container not restarting?
1. Check Azure credentials in appsettings.json
2. Verify container app name matches exactly
3. Check logs for Azure API errors

### EMS connection failed?
1. Verify ServerUrl, Username, Password
2. Test network connectivity: `telnet ems-server 7222`
3. Check EMS server is running

### Want more detailed logs?
Change Serilog MinimumLevel to "Debug" in appsettings.json

## File Structure

```
ContainerManager.Service/
├── Models/              (5 files)
├── Services/            (8 files)
├── Workers/             (MonitoringWorker.cs)
├── Configuration/       (3 settings)
├── Health/              (Health check)
├── Program.cs           (Entry point)
├── appsettings.json     (Configuration)
└── README.md            (Full documentation)
```

## Next Steps

1. Configure your EMS and Azure settings
2. Add your queue-to-container mappings
3. Run the service
4. Monitor logs for actions
5. Check NOTIFICATION.QUEUE for notifications

See **README.md** for full documentation.