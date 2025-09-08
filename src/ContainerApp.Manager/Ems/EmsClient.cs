using ContainerApp.Manager.Config;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using TIBCO.EMS;

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
    Task<bool> IsConnectedAsync();
}

public sealed class QueueObservation
{
    public string QueueName { get; init; } = string.Empty;
    public int ApproximateDepth { get; init; }
    public ConsumerPresenceStatus ConsumerPresence { get; init; } = ConsumerPresenceStatus.Unknown;
    public bool HasMessages => ApproximateDepth > 0;
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class EmsClient : IEmsClient
{
    private readonly ILogger<EmsClient> _logger;
    private readonly EmsOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private Connection? _connection;
    private Session? _session;
    private bool _disposed;

    public EmsClient(ILogger<EmsClient> logger, IOptionsMonitor<EmsOptions> options)
    {
        _logger = logger;
        _options = options.CurrentValue;
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
    }
}


