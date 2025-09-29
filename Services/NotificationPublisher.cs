using ContainerManager.Service.Configuration;
using ContainerManager.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TIBCO.EMS;

namespace ContainerManager.Service.Services;

public class NotificationPublisher : INotificationPublisher, IDisposable
{
    private readonly ILogger<NotificationPublisher> _logger;
    private readonly EmsSettings _settings;
    private readonly object _lock = new object();
    private QueueConnectionFactory? _factory;
    private QueueConnection? _connection;
    private QueueSession? _session;
    private QueueSender? _sender;
    private Queue? _queue;
    private DateTime? _lastInitAttempt;
    private int _initFailureCount = 0;
    private int _failedNotificationCount = 0;
    private volatile bool _isDisposed = false;
    private const int MAX_INIT_FAILURES = 3;
    private const int NOTIFICATION_FAILURE_WARNING_THRESHOLD = 5;
    private readonly TimeSpan INIT_RETRY_BACKOFF = TimeSpan.FromSeconds(30);

    public NotificationPublisher(
        ILogger<NotificationPublisher> logger,
        IOptions<EmsSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;

        // Try initial connection but don't throw - allow retry in PublishAsync
        try
        {
            InitializeConnection();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial notification publisher connection failed, will retry on publish");
        }
    }

    private void InitializeConnection()
    {
        try
        {
            // Dispose old connections before creating new ones to prevent resource leak
            try
            {
                _sender?.Close();
                _session?.Close();
                _connection?.Close();
            }
            catch (Exception closeEx)
            {
                _logger.LogWarning(closeEx, "Error closing previous connection during reconnect");
            }

            _factory = new QueueConnectionFactory(_settings.ServerUrl);
            _connection = _factory.CreateQueueConnection(_settings.Username, _settings.Password);
            _session = _connection.CreateQueueSession(false, Session.AUTO_ACKNOWLEDGE);
            _queue = _session.CreateQueue(_settings.NotificationQueueName);
            _sender = _session.CreateSender(_queue);
            _connection.Start();

            _logger.LogInformation("Notification publisher initialized for queue {QueueName}", _settings.NotificationQueueName);

            // Reset failure count on successful connection
            _initFailureCount = 0;
            _lastInitAttempt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize notification publisher");
            _lastInitAttempt = DateTime.UtcNow;
            _initFailureCount++;
            throw;
        }
    }

    public Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                _logger.LogWarning("Notification publisher is disposed, cannot publish notification");
                return Task.CompletedTask;
            }

            try
            {
                if (_session == null || _sender == null)
                {
                    // Check if we should retry initialization
                    if (_initFailureCount >= MAX_INIT_FAILURES)
                    {
                        // Check backoff period
                        if (_lastInitAttempt.HasValue &&
                            DateTime.UtcNow - _lastInitAttempt.Value < INIT_RETRY_BACKOFF)
                        {
                            _logger.LogWarning(
                                "Notification publisher not initialized and in backoff period, skipping notification for {ContainerApp}",
                                message.ContainerApp);
                            return Task.CompletedTask;
                        }
                    }

                    _logger.LogWarning("Notification publisher not initialized, attempting to reconnect");

                    try
                    {
                        InitializeConnection();
                    }
                    catch (Exception)
                    {
                        // Log and return - already logged in InitializeConnection
                        return Task.CompletedTask;
                    }
                }

                var json = JsonConvert.SerializeObject(message);
                var textMessage = _session!.CreateTextMessage(json);

                _sender!.Send(textMessage);

                _logger.LogInformation(
                    "Published notification: Container={ContainerApp}, Action={Action}, Status={Status}",
                    message.ContainerApp, message.Action, message.Status);

                // Reset failure counter on successful publish
                _failedNotificationCount = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish notification for container {ContainerApp}", message.ContainerApp);

                // Track consecutive failures
                _failedNotificationCount++;

                if (_failedNotificationCount >= NOTIFICATION_FAILURE_WARNING_THRESHOLD)
                {
                    _logger.LogWarning(
                        "Notification publishing has failed {FailureCount} consecutive times. Operators may not be receiving alerts.",
                        _failedNotificationCount);
                }

                // Mark connection as broken
                _session = null;
                _sender = null;
            }

            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                _sender?.Close();
                _session?.Close();
                _connection?.Close();
                _logger.LogInformation("Notification publisher closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing notification publisher");
            }
        }
    }
}