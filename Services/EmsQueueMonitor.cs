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
            _logger.LogInformation("Initializing EMS connection to {ServerUrl}", _settings.ServerUrl);

            // Initialize admin connection (synchronous TIBCO API)
            var admin = new Admin(_settings.ServerUrl, _settings.Username, _settings.Password);
            var factory = new QueueConnectionFactory(_settings.ServerUrl);

            lock (_connectionLock)
            {
                _admin = admin;
                _factory = factory;
                _isConnected = true;
            }

            _logger.LogInformation("EMS connection initialized successfully");
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