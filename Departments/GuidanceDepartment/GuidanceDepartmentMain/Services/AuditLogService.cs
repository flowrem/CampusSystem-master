using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly List<AuditEvent> events = [];
    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) { events.Add(auditEvent); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(string? entity, DateTimeOffset? since, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEvent>>(events.Where(x => (entity is null || x.Entity == entity) && (!since.HasValue || x.Timestamp >= since)).ToList());
}