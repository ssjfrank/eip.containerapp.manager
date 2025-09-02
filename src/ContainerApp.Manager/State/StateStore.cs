using Azure;
using Azure.Data.Tables;
using ContainerApp.Manager.Config;

namespace ContainerApp.Manager.State;

public interface IStateStore
{
    Task SaveAsync(string containerApp, RuntimeState state, CancellationToken cancellationToken);
    Task<RuntimeState> LoadAsync(string containerApp, CancellationToken cancellationToken);
}

public sealed class StateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "state";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTimeOffset? LastStart { get; set; }
    public DateTimeOffset? LastStop { get; set; }
    public DateTimeOffset? LastRestart { get; set; }
    public string? LastAction { get; set; }
    public string? LastActionResult { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }
    public DateTimeOffset? LastNonZeroDepthAt { get; set; }
}

public sealed class TableStateStore : IStateStore
{
    private readonly TableClient _tableClient;

    public TableStateStore(TableServiceClient tableServiceClient)
    {
        _tableClient = tableServiceClient.GetTableClient("managerstate");
        _tableClient.CreateIfNotExists();
    }

    public async Task SaveAsync(string containerApp, RuntimeState state, CancellationToken cancellationToken)
    {
        var entity = new StateEntity
        {
            RowKey = containerApp,
            LastStart = state.LastStart,
            LastStop = state.LastStop,
            LastRestart = state.LastRestart,
            LastAction = state.LastAction,
            LastActionResult = state.LastActionResult,
            CooldownUntil = state.CooldownUntil,
            LastNonZeroDepthAt = state.LastNonZeroDepthAt
        };
        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<RuntimeState> LoadAsync(string containerApp, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<StateEntity>("state", containerApp, cancellationToken: cancellationToken);
            var e = response.Value;
            return new RuntimeState
            {
                LastStart = e.LastStart,
                LastStop = e.LastStop,
                LastRestart = e.LastRestart,
                LastAction = e.LastAction,
                LastActionResult = e.LastActionResult,
                CooldownUntil = e.CooldownUntil,
                LastNonZeroDepthAt = e.LastNonZeroDepthAt
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new RuntimeState();
        }
    }
}


