using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

public sealed class GuidanceRequestRecord
{
    public int Id { get; set; }
    public int StudentId { get; init; }
    public string Subject { get; set; } = "";
    public string Details { get; set; } = "";
    public string? SafetyValveText { get; set; }
    public RequestUrgency Urgency { get; set; }
    public RequestStatus Status { get; set; }
    public int? AssignedCounselorId { get; set; }
    public string? IdempotencyKey { get; init; }
    public byte[] RowVersion { get; set; } = [1];
}

public interface IGuidanceRequestStore
{
    Task<GuidanceRequestRecord?> FindAsync(int id, CancellationToken cancellationToken);
    Task<GuidanceRequestRecord?> FindByIdempotencyKeyAsync(int studentId, string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<GuidanceRequestRecord>> ListAsync(int? studentId, TriageFilter filter, CancellationToken cancellationToken);
    Task AddAsync(GuidanceRequestRecord request, CancellationToken cancellationToken);
    Task SaveAsync(GuidanceRequestRecord request, byte[] expectedVersion, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, int studentId, byte[] expectedVersion, CancellationToken cancellationToken);
}

public interface IRefreshTokenStore
{
    Task StoreAsync(string token, string subject, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> ConsumeAsync(string token, CancellationToken cancellationToken);
}

public interface IOutboundMessageTransport
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

public sealed class UnavailableOutboundMessageTransport : IOutboundMessageTransport
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No outbound message transport is configured.");
}

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly Dictionary<string, (string Subject, DateTimeOffset Expires)> tokens = [];
    public Task StoreAsync(string token, string subject, DateTimeOffset expiresAt, CancellationToken cancellationToken) { tokens[token] = (subject, expiresAt); return Task.CompletedTask; }
    public Task<bool> ConsumeAsync(string token, CancellationToken cancellationToken) => Task.FromResult(tokens.Remove(token));
}

