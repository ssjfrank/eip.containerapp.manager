using System.Collections;
using ContainerManager.Service.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TIBCO.EMS;
using TIBCO.EMS.ADMIN;
using QueueInfoModel = ContainerManager.Service.Models.QueueInfo;

namespace ContainerManager.Service.Services;

public class EmsQueueMonitor : IEmsQueueMonitor, IDisposable
{
    private readonly ILogger<EmsQueueMonitor> _logger;
    private readonly EmsSettings _settings;
    private readonly object _connectionLock = new object();
    private Admin? _admin;
    private QueueConnectionFactory? _factory;
    private bool _isConnected;
    private Task? _initializationTask;

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _isConnected;
            }
        }
    }

    public EmsQueueMonitor(
        ILogger<EmsQueueMonitor> logger,
        IOptions<EmsSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task? existingInit;

        lock (_connectionLock)
        {
            // If already connected, return immediately
            if (_isConnected)
                return;

            // If initialization is in progress, wait for it
            if (_initializationTask != null && !_initializationTask.IsCompleted)
            {
                existingInit = _initializationTask;
            }
            else
            {
                // Start new initialization
                _initializationTask = InitializeInternalAsync(cancellationToken);
                existingInit = _initializationTask;
            }
        }

        await existingInit;
    }

    private async Task InitializeInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing EMS connection to {ServerUrl} (SSL: {IsSSL})",
                _settings.ServerUrl, _settings.IsSSL);

            // Setup SSL environment if needed
            var environment = new Hashtable();
            if (_settings.IsSSL)
            {
                SetupSslEnvironment(environment);
            }

            // Initialize admin connection (synchronous TIBCO API)
            var admin = new Admin(_settings.ServerUrl, _settings.Username, _settings.Password, environment);
            admin.AutoSave = true;

            // Create factory with SSL environment if configured
            var factory = _settings.IsSSL
                ? new QueueConnectionFactory(_settings.ServerUrl, null, environment)
                : new QueueConnectionFactory(_settings.ServerUrl);

            lock (_connectionLock)
            {
                _admin = admin;
                _factory = factory;
                _isConnected = true;
            }

            _logger.LogInformation("EMS connection initialized successfully (SSL: {IsSSL})", _settings.IsSSL);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize EMS connection");

            lock (_connectionLock)
            {
                _isConnected = false;
            }

            throw;
        }
    }

    private void SetupSslEnvironment(Hashtable environment)
    {
        try
        {
            _logger.LogDebug("Configuring SSL for EMS connection");

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

    public async Task<List<QueueInfoModel>> GetAllQueuesAsync(CancellationToken cancellationToken = default)
    {
        bool needsReconnect;

        lock (_connectionLock)
        {
            needsReconnect = !_isConnected || _admin == null;
        }

        if (needsReconnect)
        {
            _logger.LogWarning("EMS not connected, attempting to reconnect");
            await InitializeAsync(cancellationToken);
        }

        var queueInfoList = new List<QueueInfoModel>();

        try
        {
            // Get all queues using ">" wildcard pattern
            var queues = _admin!.GetQueues(">");

            if (queues != null)
            {
                foreach (TIBCO.EMS.ADMIN.QueueInfo queue in queues)
                {
                    if (queue != null)
                    {
                        // Skip system queues
                        if (queue.Name.StartsWith("$TMP$") || queue.Name.StartsWith("$sys"))
                            continue;

                        queueInfoList.Add(new QueueInfoModel
                        {
                            Name = queue.Name,
                            PendingMessageCount = queue.PendingMessageCount,
                            ReceiverCount = queue.ReceiverCount
                        });
                    }
                }
            }

            _logger.LogDebug("Retrieved {QueueCount} queues from EMS", queueInfoList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving queues from EMS");
            lock (_connectionLock)
            {
                _isConnected = false;
                _initializationTask = null; // Allow retry after failure
            }
            throw;
        }

        return queueInfoList;
    }

    public async Task<int> GetReceiverCountAsync(string queueName, CancellationToken cancellationToken = default)
    {
        bool needsReconnect;

        lock (_connectionLock)
        {
            needsReconnect = !_isConnected || _admin == null;
        }

        if (needsReconnect)
        {
            _logger.LogWarning("EMS not connected, attempting to reconnect");
            await InitializeAsync(cancellationToken);
        }

        try
        {
            var queue = _admin!.GetQueue(queueName);
            return queue?.ReceiverCount ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting receiver count for queue {QueueName}", queueName);
            return 0;
        }
    }

    public void Dispose()
    {
        lock (_connectionLock)
        {
            try
            {
                _admin?.Close();
                _factory = null;
                _isConnected = false;
                _logger.LogInformation("EMS connection closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing EMS connection");
            }
        }
    }
}