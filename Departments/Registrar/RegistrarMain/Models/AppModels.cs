namespace RegistrarMain.Models;

public class AppUser
{
    public int Id { get; set; }
    public string StudentIdNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class StudentProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Program { get; set; } = "";
    public string YearLevel { get; set; } = "";
    public string Gender { get; set; } = "";
    public string? ContactNumber { get; set; }
    public string EnrollmentStatus { get; set; } = "Active";
    public string AcademicStatus { get; set; } = "Good Standing";
    public string GuidanceClearanceStatus { get; set; } = "Cleared";
    public decimal? AverageGrade { get; set; }
    public int CompletedUnits { get; set; }
    public int TotalUnits { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class DocumentCredential
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string DocumentType { get; set; } = "";
    public string Status { get; set; } = "Missing";
    public string? Remarks { get; set; }
    public string? FilePath { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class Subject
{
    public int Id { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public int LectureHours { get; set; }
    public int LabHours { get; set; }
    public string YearLevel { get; set; } = "";
    public string Semester { get; set; } = "";
    public string Program { get; set; } = "";
    public string? Prerequisite { get; set; }
}

public class ClassOffering
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public string SectionCode { get; set; } = "";
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string InstructorName { get; set; } = "";
    public string Room { get; set; } = "";
    public string DaysOfWeek { get; set; } = "";
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Capacity { get; set; }
    public int SlotsTaken { get; set; }
    public string Status { get; set; } = "open";
}

public class RegistrarEnrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public string Program { get; set; } = "";
    public string YearLevel { get; set; } = "";
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string EnrollmentType { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

public class EnrolledSubject
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int ClassOfferingId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public string SectionCode { get; set; } = "";
    public string Status { get; set; } = "enrolled";
}

public class Grade
{
    public int Id { get; set; }
    public int EnrolledSubjectId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public string SectionCode { get; set; } = "";
    public decimal? GradeValue { get; set; }
    public bool IsInc { get; set; }
    public string Status { get; set; } = "draft";
    public string? Remarks { get; set; }
    public int? EncodedBy { get; set; }
    public DateTime? EncodedAt { get; set; }
}

public class AddDropRequest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public int EnrollmentId { get; set; }
    public string RequestType { get; set; } = "";
    public int? ClassOfferingId { get; set; }
    public string? SubjectCode { get; set; }
    public string? SectionCode { get; set; }
    public int? TargetClassOfferingId { get; set; }
    public string? TargetSubjectCode { get; set; }
    public string? TargetSectionCode { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

public class OverloadWaiverRequest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public string RequestType { get; set; } = "";
    public int RequestedUnits { get; set; }
    public string? Justification { get; set; }
    public string Status { get; set; } = "pending";
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CompletionRevisionRequest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public int GradeId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string RequestType { get; set; } = "";
    public decimal? ProposedGrade { get; set; }
    public string? Justification { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

public class TranscriptRequest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentIdNumber { get; set; } = "";
    public string Program { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Purpose { get; set; } = "";
    public int Copies { get; set; }
    public string Status { get; set; } = "pending";
    public string? QrCodeToken { get; set; }
    public string? Remarks { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}

public class ChedSpecialOrder
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string Program { get; set; } = "";
    public string? SoNumber { get; set; }
    public string? SeriesYear { get; set; }
    public string Status { get; set; } = "Applied";
    public DateTime? IssuanceDate { get; set; }
    public DateTime ApplicationDate { get; set; }
}

public class NstpSerialNumber
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string NstpType { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string? TrainingYear { get; set; }
    public string Status { get; set; } = "Issued";
    public DateTime IssuedAt { get; set; }
}

public class ActivityLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class SystemStatus
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? LastChecked { get; set; }
}

public class ApiToken
{
    public int Id { get; set; }
    public string SystemName { get; set; } = "";
    public string Token { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TransfereeCredit
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string PreviousSchool { get; set; } = "";
    public string PreviousCourseCode { get; set; } = "";
    public string PreviousCourseName { get; set; } = "";
    public int Units { get; set; }
    public decimal Grade { get; set; }
    public int? EquivalentSubjectId { get; set; }
    public string? EquivalentSubjectCode { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreditedAt { get; set; }
}

public class CourseShiftingRequest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string CurrentProgram { get; set; } = "";
    public string TargetProgram { get; set; } = "";
    public string? Reason { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
}
