using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

public sealed class CounselorTriageService(IGuidanceRequestStore store, IAuditLogService audit, IStudentDirectory students) : ICounselorTriageService
{
    public async Task<IReadOnlyList<StudentRequestResponse>> ListAsync(TriageFilter filter, CancellationToken cancellationToken) => (await store.ListAsync(null, filter, cancellationToken)).Select(ToResponse).ToList();

    public async Task<ServiceResult<StudentRequestResponse>> TransitionAsync(string actorId, TransitionRequest transition, CancellationToken cancellationToken)
    {
        var request = await store.FindAsync(transition.RequestId, cancellationToken);
        if (request is null) return ServiceResult<StudentRequestResponse>.Fail("not_found", "Request not found.");
        if (!IsValid(request.Status, transition.TargetStatus)) return ServiceResult<StudentRequestResponse>.Fail("invalid_transition", "The requested state transition is not allowed.");
        var before = request.Status.ToString(); request.Status = transition.TargetStatus;
        try { await store.SaveAsync(request, transition.RowVersion, cancellationToken); }
        catch (ConcurrencyConflictException) { return ServiceResult<StudentRequestResponse>.Fail("conflict", "Record changed; please refresh."); }
        await audit.AppendAsync(new AuditEvent(0, actorId, "status_transition", "GuidanceRequest", request.Id.ToString(), before, request.Status.ToString(), DateTimeOffset.UtcNow), cancellationToken);
        return ServiceResult<StudentRequestResponse>.Ok(ToResponse(request));
    }

    public async Task<ServiceResult<StudentRequestResponse>> ProcessIncomingReferralAsync(IncomingReferralDto referral, CancellationToken cancellationToken)
    {
        if (referral.StudentId <= 0 || !students.Exists(referral.StudentId)) return ServiceResult<StudentRequestResponse>.Fail("validation", "A valid StudentId is required.");
        if (string.IsNullOrWhiteSpace(referral.ReferralReason)) return ServiceResult<StudentRequestResponse>.Fail("validation", "ReferralReason is required.");
        if (string.IsNullOrWhiteSpace(referral.OriginatingDeptCode)) return ServiceResult<StudentRequestResponse>.Fail("validation", "OriginatingDeptCode is required.");

        var record = new GuidanceRequestRecord
        {
            StudentId = referral.StudentId,
            Subject = $"Incoming referral from {referral.OriginatingDeptCode}",
            Details = referral.ReferralReason.Trim(),
            SafetyValveText = null,
            Urgency = RequestUrgency.Normal,
            Status = RequestStatus.ReferredIn,
            IdempotencyKey = $"incoming-referral:{referral.StudentId}:{referral.OriginatingDeptCode}:{DateTimeOffset.UtcNow:O}"
        };

        await store.AddAsync(record, cancellationToken);
        return ServiceResult<StudentRequestResponse>.Ok(ToResponse(record));
    }

    private static bool IsValid(RequestStatus current, RequestStatus next) =>
        (current, next) is
        (RequestStatus.Requested, RequestStatus.InProgress)
        or (RequestStatus.Requested, RequestStatus.ReferredOut)
        or (RequestStatus.InProgress, RequestStatus.Resolved)
        or (RequestStatus.InProgress, RequestStatus.ReferredOut)
        or (RequestStatus.ReferredOut, RequestStatus.ReferredIn)
        or (RequestStatus.ReferredOut, RequestStatus.Resolved)
        or (RequestStatus.ReferredIn, RequestStatus.InProgress)
        or (RequestStatus.ReferredIn, RequestStatus.Resolved);
    private static StudentRequestResponse ToResponse(GuidanceRequestRecord x) => new(x.Id, x.StudentId, x.Subject, x.Details, x.SafetyValveText, x.Urgency, x.Status, x.AssignedCounselorId, x.RowVersion);
}
