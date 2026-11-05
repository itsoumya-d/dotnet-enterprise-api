namespace OrdersApi.Services;

public interface IIntegrationDispatcher
{
    Task DispatchAsync(Models.IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

public sealed class NoopIntegrationDispatcher : IIntegrationDispatcher
{
    public Task DispatchAsync(Models.IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
