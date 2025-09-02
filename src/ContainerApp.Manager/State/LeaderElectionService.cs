using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using System.IO;

namespace ContainerApp.Manager.State;

public interface ILeaderElectionService
{
    Task<bool> TryAcquireLeadershipAsync(CancellationToken cancellationToken);
    Task RenewAsync(CancellationToken cancellationToken);
    Task ReleaseAsync(CancellationToken cancellationToken);
    bool IsLeader { get; }
}

public sealed class LeaderElectionService : BackgroundService, ILeaderElectionService
{
    private readonly BlobLeaseClient _leaseClient;
    private readonly ILogger<LeaderElectionService> _logger;
    private string? _leaseId;

    public bool IsLeader => _leaseId is not null;

    public LeaderElectionService(BlobServiceClient blobServiceClient, ILogger<LeaderElectionService> logger)
    {
        _logger = logger;
        var container = blobServiceClient.GetBlobContainerClient("manager-state");
        container.CreateIfNotExists();
        var blob = container.GetBlobClient("leader-lease");
        if (!blob.Exists())
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            blob.Upload(stream);
        }
        _leaseClient = blob.GetBlobLeaseClient();
    }

    public async Task<bool> TryAcquireLeadershipAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _leaseClient.AcquireAsync(TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
            _leaseId = response.Value.LeaseId;
            _logger.LogInformation("Acquired leadership: {LeaseId}", _leaseId);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async Task RenewAsync(CancellationToken cancellationToken)
    {
        if (_leaseId is null) return;
        try
        {
            await _leaseClient.RenewAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Failed to renew leadership");
            _leaseId = null;
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (_leaseId is null) return;
        try
        {
            await _leaseClient.ReleaseAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            _leaseId = null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsLeader)
            {
                await TryAcquireLeadershipAsync(stoppingToken);
            }
            else
            {
                await RenewAsync(stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}


