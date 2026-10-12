using FinanceMain.Contracts.Dtos;

namespace FinanceMain.Contracts;

public class FinanceValidationException : Exception
{
    public FinanceValidationException(string message) : base(message) { }
    public FinanceValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public record AssessmentListFilter(
    string AcademicYear,
    string Semester,
    string? Status,
    string? SearchText,
    int Page,
    int PageSize);

public record ClearanceListFilter(
    string AcademicYear,
    string Semester,
    string? Status,
    string? SearchText,
    int Page,
    int PageSize);

public record PaymentListFilter(string? SearchText, int Page, int PageSize);

public record SetConditionalRequest(string Remarks);

public record RecordPaymentRequest(int FeeAssessmentId, decimal AmountPaid, string PaymentMethod, string? ReferenceNumber);

public record CreateRequestRequest(int StudentId, string DocumentType, int DocumentFeeTypeId, int Copies, string? Purpose);

public record PayRequestFeeRequest(string PaymentMethod);

public record EnrolleeAssessmentRequest(
    int StudentId,
    string StudentNumber,
    string StudentName,
    string Program,
    string AcademicYear,
    string Semester,
    int TotalUnits);

public record EnrolleeAssessmentResult(
    bool IsExisting,
    FeeAssessmentDto Assessment);

public interface IAssessmentService
{
    Task<AssessmentListResult> ListAsync(AssessmentListFilter filter);
    Task<FeeAssessmentDto?> GetByIdAsync(int id);
    Task<FeeAssessmentDto?> GetCurrentForStudentAsync(int studentId, string academicYear, string semester);
    Task<EnrolleeAssessmentResult> AssessEnrolleeAsync(EnrolleeAssessmentRequest request);
}

public interface IPaymentService
{
    Task<PaymentListResult> ListAsync(PaymentListFilter filter);
    Task<IReadOnlyList<PaymentHistoryEntryDto>> GetPaymentHistoryForStudentAsync(int studentId);
    Task<PaymentRecordDto> RecordPaymentAsync(RecordPaymentRequest request);
    Task<PaymentRowDto?> GetPaymentByNumberAsync(string paymentNumber);
}

public interface IClearanceService
{
    Task<ClearanceSummaryDto> GetSummaryAsync(string academicYear, string semester);
    Task<ClearanceListResult> ListAsync(ClearanceListFilter filter);
    Task SetConditionalAsync(int clearanceId, SetConditionalRequest request);
    Task<IReadOnlyList<StudentClearanceDto>> GetForStudentsAsync(IReadOnlyCollection<int> studentIds, string academicYear, string semester);
    Task<StudentClearanceDto?> GetStatusAsync(int studentId, string academicYear, string semester);
    Task<int> BatchIssueAsync(string academicYear, string semester);
}

public interface IRequestService
{
    Task<IReadOnlyList<DocumentFeeTypeDto>> GetDocumentFeeTypesAsync();
    Task<RequestCreateResultDto> CreateRequestAsync(CreateRequestRequest request);
    Task PayRequestFeeAsync(int requestId, PayRequestFeeRequest request);
    Task<IReadOnlyList<StudentRequestViewDto>> GetRequestsForStudentAsync(int studentId);
}

public interface IClassRosterProvider
{
    Task<ClassRosterDto?> GetRosterAsync(string sectionId);
}

public record RequestCreateResultDto(int Id, string RequestNumber);

public interface IScholarshipService
{
    Task<IReadOnlyList<ScholarshipProgramDto>> GetProgramsAsync();
    Task<IReadOnlyList<ScholarshipGranteeDto>> GetGranteesAsync();
    Task GrantAsync(string studentNumber, string scholarshipName, decimal discountAmount);
}
