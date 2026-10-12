namespace FinanceMain.Contracts.Dtos;

public record FeeAssessmentDto(
    int Id,
    string AssessmentNumber,
    string StudentDisplayName,
    string StudentNumber,
    string Program,
    string AcademicYear,
    string Semester,
    decimal TotalAmount,
    decimal TotalPaid,
    decimal Balance,
    string Status,
    DateTime AssessedDate,
    IReadOnlyList<FeeAssessmentItemDto> Items);

public record FeeAssessmentItemDto(string FeeTypeName, decimal Amount, decimal AmountPaid)
{
    public decimal Balance => Amount - AmountPaid;
};

public record ClearanceRecordDto(
    int Id,
    string StudentDisplayName,
    string Program,
    decimal? Assessed,
    decimal? Balance,
    string Status,
    DateTime? IssuedDate);

public record ClearanceSummaryDto(int TotalStudents, int Cleared, int Conditional, int NotCleared);

public record StudentClearanceDto(int StudentId, string Status);

public record PaymentRowDto(
    string PaymentNumber,
    DateTime PaymentDate,
    decimal AmountPaid,
    string PaymentMethod,
    string? ReferenceNumber,
    string Status,
    string StudentDisplayName,
    string Program,
    int FeeAssessmentId,
    string AssessmentNumber);

public record PaymentHistoryEntryDto(
    string PaymentNumber,
    DateTime PaymentDate,
    decimal AmountPaid,
    string PaymentMethod,
    string? ReferenceNumber,
    string Status);

public record PaymentRecordDto(string PaymentNumber, decimal AmountPaid, string PaymentMethod);

public record DocumentFeeTypeDto(int Id, string Name, decimal Amount);

public record StudentRequestViewDto(
    int Id,
    string RequestNumber,
    string DocumentType,
    decimal ProcessingFee,
    string Status,
    IReadOnlyList<StudentRequestPaymentDto> Payments);

public record StudentRequestPaymentDto(string Status);

public record ClassRosterDto(string SectionLabel, IReadOnlyList<ClassRosterStudentDto> Students);

public record ClassRosterStudentDto(int StudentId, string StudentDisplayName, string StudentNumber);

public record PaymentListResult(int TotalCount, IReadOnlyList<PaymentRowDto> Items);

public record ClearanceListResult(int TotalCount, IReadOnlyList<ClearanceRecordDto> Items);

public record AssessmentListResult(int TotalCount, IReadOnlyList<FeeAssessmentDto> Items);

public record ScholarshipProgramDto(string Code, string Name, string Coverage, int RecipientsCount, decimal TotalBudget);

public record ScholarshipGranteeDto(string StudentName, string StudentNumber, string Program, string ScholarshipName, decimal DiscountAmount, string Status);
