using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;

namespace FinanceMain.Services;

// Thin adapters from the page-facing service interfaces onto FinanceJsonStore.
// The store reads and writes the shared JSON file, so there is nothing to keep in memory here.

public sealed class JsonAssessmentService(FinanceJsonStore store) : IAssessmentService
{
    public Task<AssessmentListResult> ListAsync(AssessmentListFilter filter) =>
        Task.FromResult(store.GetAssessments(filter));

    public Task<FeeAssessmentDto?> GetByIdAsync(int id) =>
        Task.FromResult(store.GetAssessmentById(id));

    public Task<FeeAssessmentDto?> GetCurrentForStudentAsync(int studentId, string academicYear, string semester) =>
        Task.FromResult(store.GetCurrentAssessmentForStudent(studentId));

    public Task<EnrolleeAssessmentResult> AssessEnrolleeAsync(EnrolleeAssessmentRequest request) =>
        Task.FromResult(store.AssessEnrollee(request));
}

public sealed class JsonPaymentService(FinanceJsonStore store) : IPaymentService
{
    public Task<PaymentListResult> ListAsync(PaymentListFilter filter) =>
        Task.FromResult(store.GetPayments(filter));

    public Task<IReadOnlyList<PaymentHistoryEntryDto>> GetPaymentHistoryForStudentAsync(int studentId) =>
        Task.FromResult(store.GetPaymentHistoryForStudent(studentId));

    public Task<PaymentRecordDto> RecordPaymentAsync(RecordPaymentRequest request) =>
        Task.FromResult(store.RecordPayment(request));

    public Task<PaymentRowDto?> GetPaymentByNumberAsync(string paymentNumber) =>
        Task.FromResult(store.GetPaymentByNumber(paymentNumber));
}

public sealed class JsonClearanceService(FinanceJsonStore store) : IClearanceService
{
    public Task<ClearanceSummaryDto> GetSummaryAsync(string academicYear, string semester) =>
        Task.FromResult(store.GetClearanceSummary());

    public Task<ClearanceListResult> ListAsync(ClearanceListFilter filter) =>
        Task.FromResult(store.GetClearances(filter));

    public Task SetConditionalAsync(int clearanceId, SetConditionalRequest request)
    {
        store.SetConditional(clearanceId, request.Remarks);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StudentClearanceDto>> GetForStudentsAsync(IReadOnlyCollection<int> studentIds, string academicYear, string semester) =>
        Task.FromResult(store.GetStudentClearances(studentIds));

    public Task<StudentClearanceDto?> GetStatusAsync(int studentId, string academicYear, string semester) =>
        Task.FromResult(store.GetStudentClearance(studentId));

    public Task<int> BatchIssueAsync(string academicYear, string semester) =>
        Task.FromResult(store.BatchIssueClearance());
}

public sealed class JsonRequestService(FinanceJsonStore store) : IRequestService
{
    public Task<IReadOnlyList<DocumentFeeTypeDto>> GetDocumentFeeTypesAsync() =>
        Task.FromResult(FinanceJsonStore.DocumentFeeTypes);

    public Task<RequestCreateResultDto> CreateRequestAsync(CreateRequestRequest request) =>
        Task.FromResult(store.CreateRequest(request));

    public Task PayRequestFeeAsync(int requestId, PayRequestFeeRequest request)
    {
        store.PayRequestFee(requestId, request.PaymentMethod);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StudentRequestViewDto>> GetRequestsForStudentAsync(int studentId) =>
        Task.FromResult(store.GetRequestsForStudent(studentId));
}

public sealed class JsonScholarshipService(FinanceJsonStore store) : IScholarshipService
{
    public Task<IReadOnlyList<ScholarshipProgramDto>> GetProgramsAsync() =>
        Task.FromResult(store.GetScholarshipPrograms());

    public Task<IReadOnlyList<ScholarshipGranteeDto>> GetGranteesAsync() =>
        Task.FromResult(store.GetScholarshipGrantees());

    public Task GrantAsync(string studentNumber, string scholarshipName, decimal discountAmount)
    {
        store.GrantScholarship(studentNumber, scholarshipName, discountAmount);
        return Task.CompletedTask;
    }
}

public sealed class JsonClassRosterProvider(FinanceJsonStore store) : IClassRosterProvider
{
    public Task<ClassRosterDto?> GetRosterAsync(string sectionId) =>
        Task.FromResult(store.GetRoster(sectionId));
}
