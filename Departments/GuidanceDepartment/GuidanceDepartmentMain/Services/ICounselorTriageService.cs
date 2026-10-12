using GuidanceDepartmentMain.Contracts;

namespace GuidanceDepartmentMain.Services;

public interface ICounselorTriageService
{
    Task<IReadOnlyList<StudentRequestResponse>> ListAsync(TriageFilter filter, CancellationToken cancellationToken);
    Task<ServiceResult<StudentRequestResponse>> TransitionAsync(string actorId, TransitionRequest transition, CancellationToken cancellationToken);
    Task<ServiceResult<StudentRequestResponse>> ProcessIncomingReferralAsync(IncomingReferralDto referral, CancellationToken cancellationToken);
}