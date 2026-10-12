using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

public interface IAuditLogService
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(string? entity, DateTimeOffset? since, CancellationToken cancellationToken);
}