using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

/// <summary>Raised when a save or delete finds the stored row changed since the caller read it.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException() : base("The record changed since it was read.") { }
}

/// <summary>Lets the services check that a student exists without knowing where students are stored.</summary>
public interface IStudentDirectory
{
    bool Exists(int studentId);
    IReadOnlyList<StudentSummaryDto> List();
}

/// <summary>Students are the <c>users</c> rows with role "student".</summary>
public sealed class JsonStudentDirectory(CanonicalJsonStore store) : IStudentDirectory
{
    public bool Exists(int studentId) => Students(store.ReadRoot()).Any(u => JsonRows.Int(u, "id") == studentId);

    public IReadOnlyList<StudentSummaryDto> List() =>
        Students(store.ReadRoot())
            .Select(u => new StudentSummaryDto(JsonRows.Int(u, "id"), JsonRows.Str(u, "student_id_number"), JsonRows.Str(u, "name"), JsonRows.Str(u, "email")))
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<JsonObject> Students(JsonObject root) =>
        CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>()
            .Where(u => string.Equals(JsonRows.Str(u, "role"), "student", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Guidance requests in the <c>guidance_requests</c> collection. Student Portal writes the same rows, so a request a
/// student files there shows up here. <c>row_version</c> changes on every save for optimistic concurrency.
/// </summary>
public sealed class JsonGuidanceRequestStore(CanonicalJsonStore store) : IGuidanceRequestStore
{
    private const string Collection = "guidance_requests";

    public Task<GuidanceRequestRecord?> FindAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows(store.ReadRoot()).Where(r => JsonRows.Int(r, "id") == id).Select(Read).FirstOrDefault());

    public Task<GuidanceRequestRecord?> FindByIdempotencyKeyAsync(int studentId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Rows(store.ReadRoot())
            .Where(r => JsonRows.Int(r, "student_id") == studentId && JsonRows.Str(r, "idempotency_key") == key)
            .Select(Read).FirstOrDefault());

    public Task<IReadOnlyList<GuidanceRequestRecord>> ListAsync(int? studentId, TriageFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyList<GuidanceRequestRecord> list = Rows(store.ReadRoot()).Select(Read)
            .Where(r => (!studentId.HasValue || r.StudentId == studentId)
                        && (!filter.Status.HasValue || r.Status == filter.Status)
                        && (!filter.Urgency.HasValue || r.Urgency == filter.Urgency)
                        && (!filter.AssignedCounselorId.HasValue || r.AssignedCounselorId == filter.AssignedCounselorId))
            .OrderBy(r => r.Id)
            .ToList();
        return Task.FromResult(list);
    }

    /// <summary>Stores the request and sets its <c>Id</c>.</summary>
    public Task AddAsync(GuidanceRequestRecord request, CancellationToken cancellationToken)
    {
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, Collection);
            request.Id = CanonicalJsonStore.NextId(rows);
            request.RowVersion = [1];
            rows.Add(Write(new JsonObject { ["created_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) }, request));
        });
        return Task.CompletedTask;
    }

    public Task SaveAsync(GuidanceRequestRecord request, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        store.UpdateRoot(root =>
        {
            var row = Rows(root).FirstOrDefault(r => JsonRows.Int(r, "id") == request.Id);
            if (row is null || !VersionOf(row).SequenceEqual(expectedVersion)) throw new ConcurrencyConflictException();

            request.RowVersion = NextVersion(expectedVersion);
            Write(row, request);
        });
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(int id, int studentId, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        var deleted = false;
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, Collection);
            var row = rows.OfType<JsonObject>().FirstOrDefault(r => JsonRows.Int(r, "id") == id);
            if (row is null || JsonRows.Int(row, "student_id") != studentId) return;
            if (!VersionOf(row).SequenceEqual(expectedVersion)) throw new ConcurrencyConflictException();

            rows.Remove(row);
            deleted = true;
        });
        return Task.FromResult(deleted);
    }

    private static IEnumerable<JsonObject> Rows(JsonObject root) =>
        CanonicalJsonStore.GetArray(root, Collection).OfType<JsonObject>();

    private static GuidanceRequestRecord Read(JsonObject r) => new()
    {
        Id = JsonRows.Int(r, "id"),
        StudentId = JsonRows.Int(r, "student_id"),
        // Student Portal files "title"; the API files "subject".
        Subject = JsonRows.Str(r, "subject") is { Length: > 0 } subject ? subject : JsonRows.Str(r, "title"),
        Details = JsonRows.Str(r, "details"),
        SafetyValveText = JsonRows.Str(r, "safety_valve_text") is { Length: > 0 } safety ? safety : null,
        Urgency = Enum.TryParse<RequestUrgency>(JsonRows.Str(r, "urgency"), true, out var urgency) ? urgency : RequestUrgency.Normal,
        Status = Enum.TryParse<RequestStatus>(JsonRows.Str(r, "status"), true, out var status) ? status : RequestStatus.Requested,
        AssignedCounselorId = r["assigned_counselor_id"] is JsonValue ? JsonRows.Int(r, "assigned_counselor_id") : null,
        IdempotencyKey = JsonRows.Str(r, "idempotency_key") is { Length: > 0 } key ? key : null,
        RowVersion = VersionOf(r)
    };

    private static JsonObject Write(JsonObject row, GuidanceRequestRecord request)
    {
        row["id"] = request.Id;
        row["student_id"] = request.StudentId;
        row["subject"] = request.Subject;
        row["details"] = request.Details;
        row["safety_valve_text"] = request.SafetyValveText;
        row["urgency"] = request.Urgency.ToString();
        row["status"] = request.Status.ToString();
        row["assigned_counselor_id"] = request.AssignedCounselorId;
        row["idempotency_key"] = request.IdempotencyKey;
        row["row_version"] = Convert.ToBase64String(request.RowVersion);
        return row;
    }

    private static byte[] VersionOf(JsonObject row)
    {
        try { return Convert.FromBase64String(JsonRows.Str(row, "row_version")) is { Length: > 0 } bytes ? bytes : [1]; }
        catch (FormatException) { return [1]; }
    }

    /// <summary>Counts up one byte-wise, growing to 8 bytes before falling back to random bytes.</summary>
    private static byte[] NextVersion(byte[] current)
    {
        var next = current.Length == 0 ? new byte[] { 1 } : current.ToArray();
        for (var index = next.Length - 1; index >= 0; index--)
            if (++next[index] != 0) return next;
        return next.Length < 8 ? [.. next, 1] : RandomNumberGenerator.GetBytes(8);
    }
}

public sealed class JsonRefreshTokenStore(CanonicalJsonStore store) : IRefreshTokenStore
{
    private const string Collection = "refresh_tokens";

    public Task StoreAsync(string token, string subject, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, Collection);
            var row = rows.OfType<JsonObject>().FirstOrDefault(r => JsonRows.Str(r, "token") == token);
            if (row is null) rows.Add(row = new JsonObject { ["token"] = token });
            row["subject"] = subject;
            row["expires_at"] = expiresAt.ToString("O", CultureInfo.InvariantCulture);
        });
        return Task.CompletedTask;
    }

    /// <summary>Removes the token. True only if it existed and had not expired.</summary>
    public async Task<bool> ConsumeAsync(string token, CancellationToken cancellationToken) =>
        await RedeemAsync(token, cancellationToken) is not null;

    public Task<string?> RedeemAsync(string token, CancellationToken cancellationToken)
    {
        string? subject = null;
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, Collection);
            var row = rows.OfType<JsonObject>().FirstOrDefault(r => JsonRows.Str(r, "token") == token);
            if (row is null) return;

            rows.Remove(row);
            if (DateTimeOffset.TryParse(JsonRows.Str(row, "expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires)
                && expires > DateTimeOffset.UtcNow)
                subject = JsonRows.Str(row, "subject");
        });
        return Task.FromResult(subject);
    }
}

/// <summary>Audit trail in the shared <c>audit_events</c> collection.</summary>
public sealed class JsonAuditLogService(CanonicalJsonStore store) : IAuditLogService
{
    private const string Collection = "audit_events";

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, Collection);
            rows.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(rows),
                ["actor_id"] = auditEvent.ActorId,
                ["action"] = auditEvent.Action,
                ["entity"] = auditEvent.Entity,
                ["entity_id"] = auditEvent.EntityId,
                ["before_state"] = auditEvent.BeforeState,
                ["after_state"] = auditEvent.AfterState,
                ["timestamp"] = auditEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture)
            });
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(string? entity, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditEvent> events = CanonicalJsonStore.GetArray(store.ReadRoot(), Collection).OfType<JsonObject>()
            .Select(r => new AuditEvent(
                JsonRows.Int(r, "id"), JsonRows.Str(r, "actor_id"), JsonRows.Str(r, "action"), JsonRows.Str(r, "entity"), JsonRows.Str(r, "entity_id"),
                JsonRows.Str(r, "before_state") is { Length: > 0 } before ? before : null,
                JsonRows.Str(r, "after_state") is { Length: > 0 } after ? after : null,
                DateTimeOffset.TryParse(JsonRows.Str(r, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : default))
            .Where(e => (entity is null || e.Entity == entity) && (!since.HasValue || e.Timestamp >= since))
            .ToList();
        return Task.FromResult(events);
    }
}

internal static class JsonRows
{
    // Nodes read from disk hold JsonElements, but nodes built in this process hold the C# type
    // they were created with, so the reader accepts every numeric type.
    public static int Int(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<long>(out var l) ? (int)l
        : v.TryGetValue<decimal>(out var d) ? (int)d
        : 0;

    public static string Str(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
