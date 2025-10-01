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
    private readonly string _notificationEmailRecipient;
    private readonly HashSet<string> _operationsInProgress = new();
    private readonly Dictionary<string, DateTime> _operationStartTimes = new();
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
        _notificationEmailRecipient = _settings.NotificationEmailRecipient;
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
            // Check for and cleanup stuck operations
            CleanupStuckOperations();

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
                    _operationStartTimes[containerApp] = DateTime.UtcNow;
                }

                if (action == ContainerAction.Restart)
                {
                    _logger.LogInformation("Queuing restart operation for {ContainerApp}", containerApp);

                    try
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                // Create timeout cancellation token
                                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                var operationTimeout = TimeSpan.FromMinutes(_settings.OperationTimeoutMinutes);
                                timeoutCts.CancelAfter(operationTimeout);

                                try
                                {
                                    await HandleRestartAsync(containerApp, timeoutCts.Token);
                                }
                                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                                {
                                    _logger.LogError("Restart operation for {ContainerApp} timed out after {Timeout}",
                                        containerApp, operationTimeout);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unhandled exception in restart operation for {ContainerApp}", containerApp);
                            }
                            finally
                            {
                                lock (_operationsInProgress)
                                {
                                    _operationsInProgress.Remove(containerApp);
                                    _operationStartTimes.Remove(containerApp);
                                    _logger.LogDebug("Restart operation cleanup completed for {ContainerApp}", containerApp);
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

                        _logger.LogInformation("Restart operation task started successfully for {ContainerApp}", containerApp);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start restart operation task for {ContainerApp}, cleaning up", containerApp);

                        // CRITICAL: Cleanup immediately if Task.Run failed to start
                        lock (_operationsInProgress)
                        {
                            _operationsInProgress.Remove(containerApp);
                            _operationStartTimes.Remove(containerApp);
                        }
                    }
                }
                else if (action == ContainerAction.Stop)
                {
                    _logger.LogInformation("Queuing stop operation for {ContainerApp}", containerApp);

                    try
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                // Create timeout cancellation token
                                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                var operationTimeout = TimeSpan.FromMinutes(_settings.OperationTimeoutMinutes);
                                timeoutCts.CancelAfter(operationTimeout);

                                try
                                {
                                    await HandleStopAsync(containerApp, timeoutCts.Token);
                                }
                                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                                {
                                    _logger.LogError("Stop operation for {ContainerApp} timed out after {Timeout}",
                                        containerApp, operationTimeout);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unhandled exception in stop operation for {ContainerApp}", containerApp);
                            }
                            finally
                            {
                                lock (_operationsInProgress)
                                {
                                    _operationsInProgress.Remove(containerApp);
                                    _operationStartTimes.Remove(containerApp);
                                    _logger.LogDebug("Stop operation cleanup completed for {ContainerApp}", containerApp);
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

                        _logger.LogInformation("Stop operation task started successfully for {ContainerApp}", containerApp);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start stop operation task for {ContainerApp}, cleaning up", containerApp);

                        // CRITICAL: Cleanup immediately if Task.Run failed to start
                        lock (_operationsInProgress)
                        {
                            _operationsInProgress.Remove(containerApp);
                            _operationStartTimes.Remove(containerApp);
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

            // Get queues for this container
            var queueNames = _decisionEngine.GetQueuesForContainer(containerApp);

            // Clear idle states now that operation is actually starting
            _decisionEngine.ClearIdleStatesForQueues(queueNames);

            // Restart the container
            await _containerManager.RestartAsync(containerApp, cancellationToken);

            // Wait for receivers to appear
            var timeout = TimeSpan.FromMinutes(_settings.RestartVerificationTimeoutMinutes);
            var hasReceivers = await _containerManager.WaitForReceiversAsync(queueNames, timeout, cancellationToken);

            if (hasReceivers)
            {
                _logger.LogInformation("Container {ContainerApp} restarted successfully, receivers detected", containerApp);

                await _notificationPublisher.PublishAsync(new EmailMessage
                {
                    ToEmail = _notificationEmailRecipient,
                    Subject = $"Container Restart: SUCCESS - {containerApp}",
                    Body = $"Container '{containerApp}' restarted successfully.\n\nReceivers detected on queues: {string.Join(", ", queueNames)}\n\nTimestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
                }, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Container {ContainerApp} restarted but no receivers detected after {Timeout}",
                    containerApp, timeout);

                await _notificationPublisher.PublishAsync(new EmailMessage
                {
                    ToEmail = _notificationEmailRecipient,
                    Subject = $"Container Restart: WARNING - {containerApp}",
                    Body = $"Container '{containerApp}' restarted but no receivers detected after {timeout.TotalMinutes} minutes.\n\nQueues: {string.Join(", ", queueNames)}\n\nTimestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart container {ContainerApp}", containerApp);

            await _notificationPublisher.PublishAsync(new EmailMessage
            {
                ToEmail = _notificationEmailRecipient,
                Subject = $"Container Restart: FAILURE - {containerApp}",
                Body = $"Failed to restart container '{containerApp}'.\n\nError: {ex.Message}\n\nTimestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
            }, cancellationToken);
        }
    }

    private async Task HandleStopAsync(string containerApp, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Starting stop operation for container {ContainerApp}", containerApp);

            // Get queues for this container
            var queueNames = _decisionEngine.GetQueuesForContainer(containerApp);

            // Clear idle states now that operation is actually starting
            _decisionEngine.ClearIdleStatesForQueues(queueNames);

            await _containerManager.StopAsync(containerApp, cancellationToken);

            _logger.LogInformation("Container {ContainerApp} stopped successfully due to idle queues", containerApp);

            await _notificationPublisher.PublishAsync(new EmailMessage
            {
                ToEmail = _notificationEmailRecipient,
                Subject = $"Container Stop: SUCCESS - {containerApp}",
                Body = $"Container '{containerApp}' stopped due to idle queues.\n\nIdle queues: {string.Join(", ", queueNames)}\n\nTimestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop container {ContainerApp}", containerApp);

            await _notificationPublisher.PublishAsync(new EmailMessage
            {
                ToEmail = _notificationEmailRecipient,
                Subject = $"Container Stop: FAILURE - {containerApp}",
                Body = $"Failed to stop container '{containerApp}'.\n\nError: {ex.Message}\n\nTimestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
            }, cancellationToken);
        }
    }

    private void CleanupStuckOperations()
    {
        lock (_operationsInProgress)
        {
            // Log if there are any operations being tracked
            if (_operationStartTimes.Count > 0)
            {
                _logger.LogDebug("Checking {Count} operations for stuck detection", _operationStartTimes.Count);
            }

            var now = DateTime.UtcNow;
            var stuckOperations = new List<string>();
            var stuckOperationTimeout = TimeSpan.FromMinutes(_settings.StuckOperationCleanupMinutes);

            foreach (var kvp in _operationStartTimes)
            {
                var containerApp = kvp.Key;
                var startTime = kvp.Value;
                var duration = now - startTime;

                // Log current operation duration for visibility
                _logger.LogDebug("Operation for {ContainerApp} has been running for {Duration}",
                    containerApp, duration);

                if (duration > stuckOperationTimeout)
                {
                    stuckOperations.Add(containerApp);
                    _logger.LogWarning(
                        "Operation for {ContainerApp} has been running for {Duration} (timeout: {Timeout}), forcing cleanup",
                        containerApp, duration, stuckOperationTimeout);
                }
            }

            foreach (var containerApp in stuckOperations)
            {
                _operationsInProgress.Remove(containerApp);
                _operationStartTimes.Remove(containerApp);
                _logger.LogWarning("Forcefully cleaned up stuck operation for {ContainerApp}", containerApp);
            }
        }
    }
}