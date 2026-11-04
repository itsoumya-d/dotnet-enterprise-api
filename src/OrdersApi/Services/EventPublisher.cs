using OrdersApi.Models;

namespace OrdersApi.Services;

public interface IEventPublisher
{
    Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class NoopEventPublisher : IEventPublisher
{
    public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
