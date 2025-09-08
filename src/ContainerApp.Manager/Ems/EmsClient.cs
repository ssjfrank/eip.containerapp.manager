using ContainerApp.Manager.Config;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using TIBCO.EMS;
// Admin API references (will be available once DLLs are added)
// using TIBCO.EMS.ADMIN;

namespace ContainerApp.Manager.Ems;

public enum ConsumerPresenceStatus
{
    Unknown = 0,
    None = 1,
    Present = 2
}

public interface IEmsClient : IDisposable
{
    Task<QueueObservation> ObserveQueueAsync(string queueName, CancellationToken cancellationToken);
    Task<EnhancedQueueObservation> ObserveQueueEnhancedAsync(string queueName, CancellationToken cancellationToken);
    Task<bool> IsConnectedAsync();
    Task<bool> IsAdminConnectedAsync();
}

public sealed class QueueObservation
{
    public string QueueName { get; init; } = string.Empty;
    public int ApproximateDepth { get; init; }
    public ConsumerPresenceStatus ConsumerPresence { get; init; } = ConsumerPresenceStatus.Unknown;
    public bool HasMessages => ApproximateDepth > 0;
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

// Enhanced queue observation with admin API data
public sealed class EnhancedQueueObservation
{
    // Basic properties (compatible with existing QueueObservation)
    public string QueueName { get; init; } = string.Empty;
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    
    // Enhanced properties from Admin API
    public long PendingMessageCount { get; init; }
    public int ActiveConsumerCount { get; init; }
    public int TotalConsumerCount { get; init; }
    public DateTime? OldestMessageTimestamp { get; init; }
    public QueueStatistics Statistics { get; init; } = new();
    
    // Computed properties
    public bool HasMessages => PendingMessageCount > 0;
    public bool HasActiveConsumers => ActiveConsumerCount > 0;
    public ConsumerPresenceStatus ConsumerPresence => 
        ActiveConsumerCount > 0 ? ConsumerPresenceStatus.Present : ConsumerPresenceStatus.None;
    
    // Backwards compatibility
    public int ApproximateDepth => (int)Math.Min(PendingMessageCount, int.MaxValue);
    
    // Message age calculation
    public TimeSpan? OldestMessageAge => OldestMessageTimestamp.HasValue 
        ? DateTime.UtcNow - OldestMessageTimestamp.Value 
        : null;
}

public sealed class QueueStatistics
{
    public long TotalMessagesReceived { get; init; }
    public long TotalMessagesDelivered { get; init; }
    public long TotalMessagesAcknowledged { get; init; }
    public double MessageRate { get; init; }
    public double DeliveryRate { get; init; }
    public long BytesReceived { get; init; }
    public long BytesDelivered { get; init; }
    public int MaxConsumers { get; init; }
    public bool IsSecure { get; init; }
    public string? FailsafeMode { get; init; }
}

public sealed class EmsClient : IEmsClient
{
    private readonly ILogger<EmsClient> _logger;
    private readonly EmsOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly SemaphoreSlim _adminConnectionSemaphore = new(1, 1);
    private Connection? _connection;
    private Session? _session;
    // Admin API objects (will be uncommented once DLLs are available)
    // private Admin? _adminConnection;
    private bool _disposed;
    private bool _adminApiAvailable;

    public EmsClient(ILogger<EmsClient> logger, IOptionsMonitor<EmsOptions> options)
    {
        _logger = logger;
        _options = options.CurrentValue;
        
        // Check if admin API should be used and DLLs are available
        _adminApiAvailable = _options.UseAdminAPI && CheckAdminApiAvailability();
        
        if (_options.UseAdminAPI && !_adminApiAvailable)
        {
            _logger.LogWarning("Admin API requested but not available. Falling back to basic mode. Ensure TIBCO.EMS.ADMIN.dll is present in libs/tibco/");
        }
    }

    public async Task<QueueObservation> ObserveQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            
            if (_session == null)
            {
                _logger.LogWarning("No EMS session available for queue {Queue}", queueName);
                return CreateUnknownObservation(queueName);
            }

            var queue = _session.CreateQueue(queueName);
            var browser = _session.CreateBrowser(queue);
            
            // Count pending messages
            int messageCount = 0;
            var message = browser.GetNext();
            while (message != null)
            {
                messageCount++;
                message = browser.GetNext();
            }
            browser.Close();

            // Simplified consumer detection logic
            var consumerPresence = ConsumerPresenceStatus.Unknown;
            try
            {
                // Simple approach: if messages exist but aren't being consumed immediately,
                // assume no active consumers. This is basic but sufficient for our use case.
                // For more accurate detection, TIBCO Admin API would be needed.
                
                if (messageCount > 0)
                {
                    // Messages present - assume no consumers for simplicity
                    // (In reality, consumers might be processing slowly)
                    consumerPresence = ConsumerPresenceStatus.None;
                }
                else
                {
                    // No messages - could mean consumers processed everything
                    // or no consumers exist. We'll assume consumers are present.
                    consumerPresence = ConsumerPresenceStatus.Present;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not determine consumer presence for queue {Queue}", queueName);
                consumerPresence = ConsumerPresenceStatus.Unknown;
            }

            var observation = new QueueObservation
            {
                QueueName = queueName,
                ApproximateDepth = messageCount,
                ConsumerPresence = consumerPresence
            };

            _logger.LogDebug("Observed EMS queue {Queue}: {Depth} messages, consumers: {Consumers}, hasMessages: {HasMessages}", 
                queueName, messageCount, consumerPresence, messageCount > 0);

            return observation;
        }
        catch (EMSException ex)
        {
            _logger.LogError(ex, "EMS error observing queue {Queue}: {Error}", queueName, ex.Message);
            await HandleConnectionError();
            return CreateUnknownObservation(queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error observing queue {Queue}", queueName);
            return CreateUnknownObservation(queueName);
        }
    }

    public async Task<bool> IsConnectedAsync()
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            return _connection?.IsClosed == false && _session?.IsClosed == false;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (await IsConnectedAsync())
            return;

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (await IsConnectedAsync())
                return;

            await DisconnectInternalAsync();

            var retryPolicy = Policy
                .Handle<EMSException>()
                .Or<Exception>()
                .WaitAndRetryAsync(
                    _options.MaxReconnectAttempts,
                    retryAttempt => TimeSpan.FromMilliseconds(_options.ReconnectDelayMs),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        _logger.LogWarning("EMS connection attempt {Attempt} of {Max} failed: {Error}",
                            retryCount, _options.MaxReconnectAttempts, outcome.Exception?.Message);
                    });

            await retryPolicy.ExecuteAsync(async () =>
            {
                _logger.LogInformation("Connecting to TIBCO EMS: {ConnectionString}", MaskConnectionString(_options.ConnectionString));

                var connectionFactory = new ConnectionFactory(_options.ConnectionString);
                _connection = connectionFactory.CreateConnection(_options.Username, _options.Password);
                _connection.Start();

                _session = _connection.CreateSession(false, SessionMode.AutoAcknowledge);

                _logger.LogInformation("Successfully connected to TIBCO EMS");
            });
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task HandleConnectionError()
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        try
        {
            if (_session != null && !_session.IsClosed)
            {
                _session.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing EMS session");
        }
        finally
        {
            _session = null;
        }

        try
        {
            if (_connection != null && !_connection.IsClosed)
            {
                _connection.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing EMS connection");
        }
        finally
        {
            _connection = null;
        }
    }

    private static QueueObservation CreateUnknownObservation(string queueName)
    {
        return new QueueObservation
        {
            QueueName = queueName,
            ApproximateDepth = 0,
            ConsumerPresence = ConsumerPresenceStatus.Unknown
        };
    }

    private static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "";

        var parts = connectionString.Split(',', ';');
        return string.Join(",", parts.Select(part =>
        {
            if (part.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("pwd", StringComparison.OrdinalIgnoreCase))
            {
                var equalIndex = part.IndexOf('=');
                return equalIndex > 0 ? part[..(equalIndex + 1)] + "***" : part;
            }
            return part;
        }));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            DisconnectInternalAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during EmsClient disposal");
        }

        _connectionSemaphore?.Dispose();
        _adminConnectionSemaphore?.Dispose();
    }

    public async Task<EnhancedQueueObservation> ObserveQueueEnhancedAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_adminApiAvailable)
        {
            try
            {
                return await ObserveQueueWithAdminApiAsync(queueName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin API failed for queue {Queue}, falling back to basic mode", queueName);
                
                if (!_options.FallbackToBasicMode)
                {
                    throw;
                }
            }
        }

        // Fallback to basic mode
        var basicObservation = await ObserveQueueAsync(queueName, cancellationToken);
        return ConvertToEnhancedObservation(basicObservation);
    }

    public async Task<bool> IsAdminConnectedAsync()
    {
        if (!_adminApiAvailable)
            return false;
            
        await _adminConnectionSemaphore.WaitAsync();
        try
        {
            // This will be implemented once admin DLLs are available
            // return _adminConnection?.IsConnected == true;
            return false; // Placeholder until admin API is available
        }
        finally
        {
            _adminConnectionSemaphore.Release();
        }
    }

    private bool CheckAdminApiAvailability()
    {
        try
        {
            // Try to load the admin API assembly
            var assembly = System.Reflection.Assembly.LoadFrom("TIBCO.EMS.ADMIN.dll");
            return assembly != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<EnhancedQueueObservation> ObserveQueueWithAdminApiAsync(string queueName, CancellationToken cancellationToken)
    {
        await EnsureAdminConnectedAsync(cancellationToken);
        
        // This is placeholder implementation - will be completed once admin DLLs are available
        // The actual implementation would look like:
        /*
        var queueInfo = await _adminConnection.GetQueueInfoAsync(queueName);
        var consumers = await _adminConnection.GetConsumersAsync(queueName);
        
        return new EnhancedQueueObservation
        {
            QueueName = queueName,
            PendingMessageCount = queueInfo.PendingMessageCount,
            ActiveConsumerCount = consumers.Count(c => c.IsActive),
            TotalConsumerCount = consumers.Count,
            OldestMessageTimestamp = queueInfo.OldestMessageTimestamp,
            Statistics = new QueueStatistics
            {
                TotalMessagesReceived = queueInfo.TotalReceived,
                TotalMessagesDelivered = queueInfo.TotalDelivered,
                MessageRate = queueInfo.InboundMessageRate,
                DeliveryRate = queueInfo.OutboundMessageRate,
                // ... other statistics
            }
        };
        */
        
        _logger.LogDebug("Admin API implementation pending - using fallback for queue {Queue}", queueName);
        var basicObservation = await ObserveQueueAsync(queueName, cancellationToken);
        return ConvertToEnhancedObservation(basicObservation);
    }

    private async Task EnsureAdminConnectedAsync(CancellationToken cancellationToken)
    {
        if (!_adminApiAvailable)
            throw new InvalidOperationException("Admin API not available");

        // Placeholder implementation - will be completed once admin DLLs are available
        /*
        if (_adminConnection?.IsConnected == true)
            return;

        await _adminConnectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_adminConnection?.IsConnected == true)
                return;

            await DisconnectAdminInternalAsync();

            _logger.LogInformation("Connecting to TIBCO EMS Admin API: {ConnectionString}", MaskConnectionString(_options.ConnectionString));
            
            _adminConnection = new Admin(_options.ConnectionString);
            await _adminConnection.ConnectAsync(_options.AdminUsername, _options.AdminPassword);
            
            _logger.LogInformation("Successfully connected to TIBCO EMS Admin API");
        }
        finally
        {
            _adminConnectionSemaphore.Release();
        }
        */
        
        // For now, just ensure regular connection is available
        await EnsureConnectedAsync(cancellationToken);
    }

    private EnhancedQueueObservation ConvertToEnhancedObservation(QueueObservation basic)
    {
        return new EnhancedQueueObservation
        {
            QueueName = basic.QueueName,
            ObservedAt = basic.ObservedAt,
            PendingMessageCount = basic.ApproximateDepth,
            // Estimate consumer presence based on basic observation
            ActiveConsumerCount = basic.ConsumerPresence == ConsumerPresenceStatus.Present ? 1 : 0,
            TotalConsumerCount = basic.ConsumerPresence == ConsumerPresenceStatus.Present ? 1 : 0,
            OldestMessageTimestamp = null, // Not available in basic mode
            Statistics = new QueueStatistics() // Empty statistics in basic mode
        };
    }
}


