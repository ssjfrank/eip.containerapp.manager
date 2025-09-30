namespace ContainerManager.Service.Services;

public interface IContainerManager : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(string containerAppName, CancellationToken cancellationToken = default);
    Task StopAsync(string containerAppName, CancellationToken cancellationToken = default);
    Task<bool> WaitForReceiversAsync(List<string> queueNames, TimeSpan timeout, CancellationToken cancellationToken = default);
}