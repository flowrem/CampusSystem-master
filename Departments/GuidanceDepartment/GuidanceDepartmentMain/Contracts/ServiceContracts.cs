using System.Security.Claims;

namespace GuidanceDepartmentMain.Contracts;

public record ServiceResult<T>(bool Succeeded, T? Value, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static ServiceResult<T> Ok(T value) => new(true, value);
    public static ServiceResult<T> Fail(string code, string message) => new(false, default, code, message);
}

public enum RequestStatus { Requested, InProgress, Resolved, ReferredOut, ReferredIn }
public enum RequestUrgency { Normal, Priority, Urgent }

public record StudentRequestDto(string Subject, string Details, string? SafetyValveText, RequestUrgency Urgency);
public record StudentRequestResponse(int Id, int StudentId, string Subject, string Details, string? SafetyValveText, RequestUrgency Urgency, RequestStatus Status, int? AssignedCounselorId, byte[] RowVersion);
public record TriageFilter(RequestStatus? Status, RequestUrgency? Urgency, int? AssignedCounselorId);
public record TransitionRequest(int RequestId, RequestStatus TargetStatus, byte[] RowVersion);
public record AuditEvent(int Id, string ActorId, string Action, string Entity, string EntityId, string? BeforeState, string? AfterState, DateTimeOffset Timestamp);
public record AuthRequest(string UserName, string Password, string Role);
public record AuthResponse(string Role, DateTimeOffset ExpiresAt);
public record ImportRowError(int Row, string Field, string Message);
public record ImportValidationResult(bool Passed, int RowCount, IReadOnlyList<ImportRowError> Errors, IReadOnlyList<string> SchemaDrift);
public record NotificationMessage(string Recipient, string Subject, string Body);
public record RedactionRules(bool Emails = true, bool PhoneNumbers = true, bool SsnLikeValues = true);

public static class ClaimsPrincipalExtensions
{
    public static int? GetStudentId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue("StudentId"), out var id) ? id : null;
}
