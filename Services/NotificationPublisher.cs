using System.Collections;
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
    private TIBCO.EMS.Queue? _queue;
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
            _logger.LogInformation("Initializing notification publisher for queue {QueueName} (SSL: {IsSSL})",
                _settings.NotificationQueueName, _settings.IsSSL);

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

            // Setup SSL environment if needed
            var environment = new Hashtable();
            if (_settings.IsSSL)
            {
                SetupSslEnvironment(environment);
            }

            // Create factory with SSL environment if configured
            _factory = _settings.IsSSL
                ? new QueueConnectionFactory(_settings.ServerUrl, null, environment)
                : new QueueConnectionFactory(_settings.ServerUrl);

            _connection = _factory.CreateQueueConnection(_settings.Username, _settings.Password);
            _session = _connection.CreateQueueSession(false, Session.AUTO_ACKNOWLEDGE);
            _queue = _session.CreateQueue(_settings.NotificationQueueName);
            _sender = _session.CreateSender(_queue);
            _connection.Start();

            _logger.LogInformation("Notification publisher initialized successfully for queue {QueueName} (SSL: {IsSSL})",
                _settings.NotificationQueueName, _settings.IsSSL);

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

    private void SetupSslEnvironment(Hashtable environment)
    {
        try
        {
            _logger.LogDebug("Configuring SSL for notification publisher");

            var storeInfo = new EMSSSLFileStoreInfo();

            // Enable SSL trace if requested
            if (_settings.SslTrace)
            {
                _logger.LogDebug("SSL tracing enabled");
                environment.Add(EMSSSL.TRACE, true);
            }

            // Set SSL target hostname if specified
            if (!string.IsNullOrEmpty(_settings.SslTargetHostName))
            {
                _logger.LogDebug("Setting SSL target hostname: {TargetHostName}", _settings.SslTargetHostName);
                environment.Add(EMSSSL.TARGET_HOST_NAME, _settings.SslTargetHostName);
            }

            // Configure certificate verification warnings
            if (!_settings.VerifyServerCertificate)
            {
                _logger.LogWarning("SSL server certificate verification is DISABLED - only use for testing!");
            }

            if (!_settings.VerifyHostName)
            {
                _logger.LogWarning("SSL hostname verification is DISABLED - only use for testing!");
            }

            // Configure trust store if provided
            if (!string.IsNullOrEmpty(_settings.TrustStorePath))
            {
                _logger.LogDebug("Configuring trust store: {TrustStorePath}", _settings.TrustStorePath);
                storeInfo.SetSSLTrustedCertificate(_settings.TrustStorePath);
            }

            // Configure client certificate if provided
            if (!string.IsNullOrEmpty(_settings.ClientCertificatePath))
            {
                _logger.LogDebug("Configuring client certificate: {CertPath}", _settings.ClientCertificatePath);
                storeInfo.SetSSLClientIdentity(_settings.ClientCertificatePath);

                if (!string.IsNullOrEmpty(_settings.ClientCertificatePassword))
                {
                    storeInfo.SetSSLPassword(_settings.ClientCertificatePassword.ToCharArray());
                }
            }

            // Add store info and type to environment
            environment.Add(EMSSSL.STORE_INFO, storeInfo);
            environment.Add(EMSSSL.STORE_TYPE, EMSSSLStoreType.EMSSSL_STORE_TYPE_FILE);

            _logger.LogDebug("SSL configuration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring SSL");
            throw;
        }
    }

    public Task PublishAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        // Quick disposal check without holding lock
        if (_isDisposed)
        {
            _logger.LogWarning("Notification publisher is disposed, cannot publish notification");
            return Task.CompletedTask;
        }

        try
        {
            // Check if initialization needed (read-only check, no lock needed for volatile fields)
            bool needsInit;
            bool inBackoff = false;

            lock (_lock)
            {
                needsInit = _session == null || _sender == null;

                if (needsInit && _initFailureCount >= MAX_INIT_FAILURES)
                {
                    // Check backoff period
                    if (_lastInitAttempt.HasValue &&
                        DateTime.UtcNow - _lastInitAttempt.Value < INIT_RETRY_BACKOFF)
                    {
                        inBackoff = true;
                    }
                }
            }

            if (inBackoff)
            {
                _logger.LogWarning(
                    "Notification publisher not initialized and in backoff period, skipping notification to {ToEmail}",
                    message.ToEmail);
                return Task.CompletedTask;
            }

            // Initialize connection if needed - I/O operation done OUTSIDE lock
            if (needsInit)
            {
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

            // Serialize message - done OUTSIDE lock (no shared state accessed)
            var json = JsonConvert.SerializeObject(message);

            // Capture session and sender references inside lock, send outside lock
            QueueSession sessionRef;
            QueueSender senderRef;

            lock (_lock)
            {
                // Re-check disposal state after potential wait
                if (_isDisposed)
                {
                    _logger.LogWarning("Notification publisher disposed during publish operation");
                    return Task.CompletedTask;
                }

                // Verify session/sender still valid (could have been cleared by another thread)
                if (_session == null || _sender == null)
                {
                    _logger.LogWarning("Notification publisher connection lost before send, skipping notification");
                    return Task.CompletedTask;
                }

                // Capture references to use outside lock
                sessionRef = _session;
                senderRef = _sender;
            }

            // Perform I/O operations OUTSIDE lock to prevent deadlock
            var textMessage = sessionRef.CreateTextMessage(json);
            senderRef.Send(textMessage);

            _logger.LogInformation(
                "Published email notification: To={ToEmail}, Subject={Subject}",
                message.ToEmail, message.Subject);

            // Reset failure counter on successful publish
            lock (_lock)
            {
                _failedNotificationCount = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish email notification to {ToEmail}", message.ToEmail);

            // Track consecutive failures and mark connection as broken
            lock (_lock)
            {
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
        }

        return Task.CompletedTask;
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