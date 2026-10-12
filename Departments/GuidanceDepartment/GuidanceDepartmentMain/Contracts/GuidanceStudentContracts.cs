namespace GuidanceDepartmentMain.Contracts;

public record StudentSummaryDto(int Id, string StudentNumber, string FullName, string Email);
public record IncomingReferralDto(int StudentId, string ReferralReason, string OriginatingDeptCode);
