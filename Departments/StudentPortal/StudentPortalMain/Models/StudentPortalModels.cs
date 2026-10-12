namespace StudentPortalMain.Models;

public class StudentUser
{
    public int Id { get; set; }
    public string StudentIdNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "student";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
}

public class StudentProfileData
{
    public int UserId { get; set; }
    public string Program { get; set; } = "BS Information Technology";
    public string YearLevel { get; set; } = "3rd Year";
    public string CurriculumYear { get; set; } = "2023-2027";
    public decimal? AverageGrade { get; set; }
    public string AcademicStatus { get; set; } = "Good Standing";
    public string EnrollmentStatus { get; set; } = "Regular / Enrolled";
    public int CompletedUnits { get; set; }
    public int UnitsRemaining { get; set; } = 144;
    public string GuidanceClearanceStatus { get; set; } = "Cleared";
    public string? GuidanceNotes { get; set; }
    public string? ContactNumber { get; set; }
    public string? Address { get; set; }
    public DateTime? BirthDate { get; set; }
    public string Gender { get; set; } = "Female";
}

public class DocumentCredentialItem
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string DocumentType { get; set; } = "";
    public string Status { get; set; } = "Missing"; // Missing, Submitted, Verified
    public string? Remarks { get; set; }
    public string? FilePath { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class EnrolledClassItem
{
    public int EnrolledSubjectId { get; set; }
    public int ClassOfferingId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public int LecHours { get; set; }
    public int LabHours { get; set; }
    public string SectionCode { get; set; } = "";
    public string InstructorName { get; set; } = "TBA";
    public string Room { get; set; } = "TBA";
    public string DaysOfWeek { get; set; } = "";
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string Status { get; set; } = "enrolled";

    public string FormattedSchedule
    {
        get
        {
            var start = DateTime.Today.Add(StartTime).ToString("hh:mm tt");
            var end = DateTime.Today.Add(EndTime).ToString("hh:mm tt");
            return $"{DaysOfWeek} {start} - {end}";
        }
    }
}

public class StudentGradeItem
{
    public int GradeId { get; set; }
    public int EnrolledSubjectId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int Units { get; set; }
    public string SectionCode { get; set; } = "";
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public decimal? Grade { get; set; }
    public bool IsInc { get; set; }
    public string GradeStatus { get; set; } = "draft";
    public string? Remarks { get; set; }

    public string GradeDisplay
    {
        get
        {
            if (IsInc) return "INC";
            if (Grade.HasValue) return Grade.Value.ToString("0.00");
            return "NGS"; // No Grade Submitted yet
        }
    }

    public string StatusBadgeClass
    {
        get
        {
            if (IsInc) return "badge-warning";
            if (!Grade.HasValue) return "badge-secondary";
            if (Grade.Value <= 3.00m) return "badge-success";
            return "badge-danger";
        }
    }
}

public class DocumentRequestRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string DocumentType { get; set; } = "";
    public string? Purpose { get; set; }
    public int Copies { get; set; } = 1;
    public string Status { get; set; } = "pending"; // pending, processing, ready, released, rejected
    public string QrCodeToken { get; set; } = "";
    public string? Remarks { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}

public class AddDropRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int EnrollmentId { get; set; }
    public string RequestType { get; set; } = ""; // add, drop, change
    public int? ClassOfferingId { get; set; }
    public string? SubjectCode { get; set; }
    public string? SectionCode { get; set; }
    public int? TargetClassOfferingId { get; set; }
    public string? TargetSubjectCode { get; set; }
    public string? TargetSectionCode { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "pending"; // pending, approved, rejected
    public DateTime CreatedAt { get; set; }
}

public class OverloadRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string RequestType { get; set; } = "overload";
    public int RequestedUnits { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime RequestedAt { get; set; }
}

public class CompletionRevisionRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int GradeId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string RequestType { get; set; } = "completion"; // completion, revision
    public decimal RequestedGrade { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime RequestedAt { get; set; }
}

public class StudentEnrollmentOverview
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string EnrollmentType { get; set; } = "Regular";
    public string Status { get; set; } = "pending"; // pending, validated, enrolled, dropped
    public DateTime EnrolledAt { get; set; }
    public int TotalUnits { get; set; }
    public int SubjectCount { get; set; }
    public List<EnrolledClassItem> Subjects { get; set; } = new();
}

public class TermGradeGroup
{
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public List<StudentGradeItem> Grades { get; set; } = new();
    public decimal? SemesterGpa { get; set; }
    public int TotalUnits { get; set; }
}

public class CourseShiftingRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string FromProgram { get; set; } = "";
    public string ToProgram { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending, approved, rejected
    public DateTime RequestedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? EvaluatedBy { get; set; }
}

public class ClearanceRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string DepartmentName { get; set; } = ""; // Registrar, Guidance, University Library Department, Finance, Academic Dean
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public string Status { get; set; } = "Cleared"; // Cleared, Pending, Deficient
    public string ClearedBy { get; set; } = "Officer";
    public DateTime? ClearedAt { get; set; }
    public string? Remarks { get; set; }
}

public class PaymentRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string ReceiptNo { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime PayDate { get; set; }
    public string PaymentMethod { get; set; } = "Online Banking"; // GCash, Maya, Over-the-counter, Credit Card
    public string PaymentCategory { get; set; } = "Tuition"; // Tuition, Downpayment, Midterm, Final, Library Fine, Document Fee
    public string Status { get; set; } = "Paid";
    public string Description { get; set; } = "Tuition / Matriculation Fee";
}

public class LibraryBookItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Isbn { get; set; } = "";
    public string CallNumber { get; set; } = "";
    public string Category { get; set; } = "General Reference";
    public int AvailableCopies { get; set; } = 1;
    public int TotalCopies { get; set; } = 1;
    public string ShelfLocation { get; set; } = "Main Stacks";
}

public class BookLoanRecord
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string BookAuthor { get; set; } = "";
    public int StudentId { get; set; }
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ConditionOnReturn { get; set; } // "Good", "Damaged", "Lost"
    public string Status { get; set; } = "Borrowed"; // Borrowed, Returned, Overdue, Damaged, Lost
    public decimal FineAmount { get; set; }
    public string FineStatus { get; set; } = "None"; // None, Pending, Paid
    public string? Remarks { get; set; }
}

public class LibraryReservationRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string BookTitle { get; set; } = "";
    public string BookAuthor { get; set; } = "";
    public DateTime ReservationDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Active"; // Active, Returned, Overdue, Damaged
}

public class ScholarshipItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SlotsAvailable { get; set; }
}

public class ScholarshipApplicationRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string ScholarshipName { get; set; } = "";
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public bool RequirementsSubmitted { get; set; } = true;
    public string Status { get; set; } = "pending"; // pending, approved, shortlisted, rejected
    public DateTime RequestedAt { get; set; }
}

public class AssignmentItem
{
    public int Id { get; set; }
    public int ClassOfferingId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal MaxScore { get; set; } = 100;
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Assigned";
    public bool IsSubmitted { get; set; }
    public decimal? MyScore { get; set; }
    public string Type { get; set; } = "";
    public bool IsClosed { get; set; }
    public bool IsLate { get; set; }
    public string SubmissionText { get; set; } = "";
    public string Feedback { get; set; } = "";
}
