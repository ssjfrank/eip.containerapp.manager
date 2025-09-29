using ContainerManager.Service.Configuration;
using ContainerManager.Service.Models;
using ContainerManager.Service.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContainerManager.Service.Workers;

public class MonitoringWorker : BackgroundService
{
    private readonly ILogger<MonitoringWorker> _logger;
    private readonly ManagerSettings _settings;
    private readonly IEmsQueueMonitor _emsMonitor;
    private readonly IDecisionEngine _decisionEngine;
    private readonly IContainerManager _containerManager;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly HashSet<string> _operationsInProgress = new();
    private readonly List<Task> _backgroundTasks = new();
    private readonly object _taskLock = new();
    private CancellationTokenSource? _shutdownCts;
    // _cleanupCounter must be accessed only within _taskLock to ensure thread safety
    private int _cleanupCounter = 0;
    private const int CLEANUP_THRESHOLD = 10;

    public MonitoringWorker(
        ILogger<MonitoringWorker> logger,
        IOptions<ManagerSettings> settings,
        IEmsQueueMonitor emsMonitor,
        IDecisionEngine decisionEngine,
        IContainerManager containerManager,
        INotificationPublisher notificationPublisher)
    {
        _logger = logger;
        _settings = settings.Value;
        _emsMonitor = emsMonitor;
        _decisionEngine = decisionEngine;
        _containerManager = containerManager;
        _notificationPublisher = notificationPublisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ContainerManager MonitoringWorker starting");

        bool isInitialized = false;
        int initRetryCount = 0;
        const int MAX_INIT_RETRIES = 3;

        // Try to initialize with retries
        while (!isInitialized && initRetryCount < MAX_INIT_RETRIES && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _emsMonitor.InitializeAsync(stoppingToken);
                await _containerManager.InitializeAsync(stoppingToken);

                isInitialized = true;
                _logger.LogInformation("All services initialized successfully");
            }
            catch (Exception ex)
            {
                initRetryCount++;
                _logger.LogError(ex, "Failed to initialize services (attempt {Attempt}/{MaxAttempts})",
                    initRetryCount, MAX_INIT_RETRIES);

                if (initRetryCount >= MAX_INIT_RETRIES)
                {
                    _logger.LogCritical("Failed to initialize after {MaxAttempts} attempts, stopping service",
                        MAX_INIT_RETRIES);
                    throw;
                }

                // Wait before retrying with exponential backoff
                await Task.Delay(TimeSpan.FromSeconds(5 * initRetryCount), stoppingToken);
            }
        }

        if (!isInitialized)
        {
            _logger.LogCritical("Service initialization failed, exiting");
            return;
        }

        var pollingInterval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);

        // Create linked cancellation token for background operations
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var workToken = _shutdownCts.Token;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorAndActAsync(workToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in monitoring loop, will retry");
                }

                try
                {
                    await Task.Delay(pollingInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // Cancel any in-flight operations
            _shutdownCts?.Cancel();

            // Wait for all background tasks to complete with timeout
            Task[] tasks;
            lock (_taskLock)
            {
                tasks = _backgroundTasks.ToArray();
            }

            if (tasks.Length > 0)
            {
                _logger.LogInformation("Waiting for {TaskCount} background operations to complete", tasks.Length);
                try
                {
                    var completionTask = Task.WhenAll(tasks);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                    var completedTask = await Task.WhenAny(completionTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.LogWarning("Timeout waiting for background operations to complete, some may still be running");
                        // Note: Tasks will continue running but we're not tracking them post-shutdown
                        // This is acceptable as they're cleanup operations
                    }
                    else
                    {
                        // Check for exceptions even on successful completion
                        if (completionTask.IsFaulted)
                        {
                            _logger.LogWarning(completionTask.Exception, "Some background operations completed with errors during shutdown");
                        }
                        else
                        {
                            _logger.LogInformation("All background operations completed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Some background operations completed with errors during shutdown");
                }
            }

            _shutdownCts?.Dispose();
        }

        _logger.LogInformation("ContainerManager MonitoringWorker stopping");
    }

    private async Task MonitorAndActAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Get all queue states from EMS
            _logger.LogDebug("Retrieving queue information from EMS");
            var queues = await _emsMonitor.GetAllQueuesAsync(cancellationToken);

            if (queues.Count == 0)
            {
                _logger.LogWarning("No queues found in EMS");
                return;
            }

            _logger.LogDebug("Retrieved {QueueCount} queues, analyzing for actions", queues.Count);

            // Step 2: Decide actions based on business rules
            var actions = await _decisionEngine.DecideActionsAsync(queues, cancellationToken);

            // Step 3: Execute actions (fire-and-forget to not block monitoring loop)
            foreach (var (containerApp, action) in actions)
            {
                // Check if operation already in progress for this container
                lock (_operationsInProgress)
                {
                    if (_operationsInProgress.Contains(containerApp))
                    {
                        _logger.LogDebug("Operation already in progress for {ContainerApp}, skipping", containerApp);
                        continue;
                    }
                    _operationsInProgress.Add(containerApp);
                }

                if (action == ContainerAction.Restart)
                {
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleRestartAsync(containerApp, cancellationToken);
                        }
                        finally
                        {
                            lock (_operationsInProgress)
                            {
                                _operationsInProgress.Remove(containerApp);
                            }
                        }
                    }, cancellationToken);

                    lock (_taskLock)
                    {
                        _backgroundTasks.Add(task);

                        // Only cleanup periodically to avoid O(n) operation on every add
                        _cleanupCounter++;
                        if (_cleanupCounter >= CLEANUP_THRESHOLD)
                        {
                            _backgroundTasks.RemoveAll(t => t.IsCompleted);
                            _cleanupCounter = 0;
                        }
                    }
                }
                else if (action == ContainerAction.Stop)
                {
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleStopAsync(containerApp, cancellationToken);
                        }
                        finally
                        {
                            lock (_operationsInProgress)
                            {
                                _operationsInProgress.Remove(containerApp);
                            }
                        }
                    }, cancellationToken);

                    lock (_taskLock)
                    {
                        _backgroundTasks.Add(task);

                        // Only cleanup periodically to avoid O(n) operation on every add
                        _cleanupCounter++;
                        if (_cleanupCounter >= CLEANUP_THRESHOLD)
                        {
                            _backgroundTasks.RemoveAll(t => t.IsCompleted);
                            _cleanupCounter = 0;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during monitor and act cycle");
        }
    }

    private async Task HandleRestartAsync(string containerApp, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Starting restart operation for container {ContainerApp}", containerApp);

            // Restart the container
            await _containerManager.RestartAsync(containerApp, cancellationToken);

            // Get queues for this container
            var queueNames = _decisionEngine.GetQueuesForContainer(containerApp);

            // Wait for receivers to appear
            var timeout = TimeSpan.FromMinutes(_settings.RestartVerificationTimeoutMinutes);
            var hasReceivers = await _containerManager.WaitForReceiversAsync(queueNames, timeout, cancellationToken);

            if (hasReceivers)
            {
                _logger.LogInformation("Container {ContainerApp} restarted successfully, receivers detected", containerApp);

                await _notificationPublisher.PublishAsync(new NotificationMessage
                {
                    ContainerApp = containerApp,
                    Action = "RESTART",
                    Status = "SUCCESS",
                    Message = $"Container restarted successfully, receivers detected on queues: {string.Join(", ", queueNames)}",
                    QueueName = string.Join(", ", queueNames)
                }, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Container {ContainerApp} restarted but no receivers detected after {Timeout}",
                    containerApp, timeout);

                await _notificationPublisher.PublishAsync(new NotificationMessage
                {
                    ContainerApp = containerApp,
                    Action = "RESTART",
                    Status = "WARNING",
                    Message = $"Container restarted but no receivers detected after {timeout.TotalMinutes} minutes",
                    QueueName = string.Join(", ", queueNames)
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart container {ContainerApp}", containerApp);

            await _notificationPublisher.PublishAsync(new NotificationMessage
            {
                ContainerApp = containerApp,
                Action = "RESTART",
                Status = "FAILURE",
                Message = $"Failed to restart container: {ex.Message}",
                QueueName = string.Empty
            }, cancellationToken);
        }
    }

    private async Task HandleStopAsync(string containerApp, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Starting stop operation for container {ContainerApp}", containerApp);

            await _containerManager.StopAsync(containerApp, cancellationToken);

            var queueNames = _decisionEngine.GetQueuesForContainer(containerApp);

            _logger.LogInformation("Container {ContainerApp} stopped successfully due to idle queues", containerApp);

            await _notificationPublisher.PublishAsync(new NotificationMessage
            {
                ContainerApp = containerApp,
                Action = "STOP",
                Status = "SUCCESS",
                Message = $"Container stopped due to idle queues: {string.Join(", ", queueNames)}",
                QueueName = string.Join(", ", queueNames)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop container {ContainerApp}", containerApp);

            await _notificationPublisher.PublishAsync(new NotificationMessage
            {
                ContainerApp = containerApp,
                Action = "STOP",
                Status = "FAILURE",
                Message = $"Failed to stop container: {ex.Message}",
                QueueName = string.Empty
            }, cancellationToken);
        }
    }
}