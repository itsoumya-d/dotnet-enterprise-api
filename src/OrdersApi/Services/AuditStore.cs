using OrdersApi.Models;

namespace OrdersApi.Services;

public interface IAuditStore
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> ListAsync(int limit, CancellationToken cancellationToken = default);
}

public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<AuditEvent> _events = new();
    private readonly object _gate = new();

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _events.Add(auditEvent);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AuditEvent> result = _events
                .OrderByDescending(item => item.OccurredAt)
                .Take(limit <= 0 ? 100 : limit)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
