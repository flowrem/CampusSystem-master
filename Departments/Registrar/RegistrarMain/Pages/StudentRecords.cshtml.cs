using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Models;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class StudentRecordsModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly CanonicalJsonStore _store;
    private readonly ILogger<StudentRecordsModel> _logger;

    public StudentRecordsModel(DatabaseService db, CanonicalJsonStore store, ILogger<StudentRecordsModel> logger)
    {
        _db = db;
        _store = store;
        _logger = logger;
    }

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string? TemporaryPassword { get; private set; }
    public string CanonicalPath => "campus_system_unified_data_schema.json (repository root)";
    public bool DemoMode { get; private set; }
    public List<DemoStudent> DemoStudents { get; } = new();

    public static readonly string[] VaultDocumentTypes = [
        "Form 137",
        "Form 138",
        "Birth Certificate",
        "Good Moral",
        "Transcript from Previous School",
        "Medical Clearance"
    ];

    public static readonly string[] EnrollmentStatuses = ["Active", "On-Leave", "Dropped", "Dismissed", "Graduated"];
    public static readonly string[] AcademicStatuses = ["Good Standing", "Probation", "Graduating", "Graduated", "Disqualified"];

    public int ViewId { get; set; }
    public string Query { get; set; } = "";
    public string ProgramFilter { get; set; } = "";
    public string StatusFilter { get; set; } = "";

    // List view
    public List<StudentListItem> Students { get; set; } = new();
    public List<string> ProgramsList { get; set; } = new();

    // Detail view
    public AppUser? Student { get; set; }
    public StudentProfileDetail Profile { get; set; } = new();
    public List<DocumentCredential> Vault { get; set; } = new();
    public List<StudentGradeRecord> StudentGrades { get; set; } = new();
    public List<TransfereeCreditedRecord> CreditedSubjects { get; set; } = new();
    public string FinanceClearanceStatus { get; set; } = "Cleared";
    public decimal TotalPaidAmount { get; set; } = 0;

    public class StudentListItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Program { get; set; }
        public string? YearLevel { get; set; }
        public string? EnrollmentStatus { get; set; }
        public string? AcademicStatus { get; set; }
        public decimal? AverageGrade { get; set; }
        public int VerifiedCount { get; set; }
        public int TotalDocs { get; set; }
    }

    public class StudentProfileDetail
    {
        public string Program { get; set; } = "Not set";
        public string YearLevel { get; set; } = "1st Year";
        public decimal? AverageGrade { get; set; }
        public string AcademicStatus { get; set; } = "Good Standing";
        public string EnrollmentStatus { get; set; } = "Active";
        public int CompletedUnits { get; set; }
        public int UnitsRemaining { get; set; } = 144;
        public string GuidanceClearanceStatus { get; set; } = "Cleared";
        public string? ContactNumber { get; set; }
        public string? Address { get; set; }
        public string Gender { get; set; } = "Female";
    }

    public class StudentGradeRecord
    {
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public int Units { get; set; }
        public string SectionCode { get; set; } = "";
        public string SchoolYear { get; set; } = "";
        public string Semester { get; set; } = "";
        public decimal? Grade { get; set; }
        public bool IsInc { get; set; }
        public string? GradeStatus { get; set; }
        public string? Remarks { get; set; }
    }

    public class TransfereeCreditedRecord
    {
        public string PrevSchool { get; set; } = "";
        public string PrevSubjectCode { get; set; } = "";
        public string PrevSubjectTitle { get; set; } = "";
        public int PrevUnits { get; set; }
        public decimal PrevGrade { get; set; }
        public string InstitutionalCode { get; set; } = "";
        public string InstitutionalName { get; set; } = "";
        public string Status { get; set; } = "Approved";
    }

    public IActionResult OnGet(int id = 0, string q = "", string program = "", string status = "")
    {
        _db.EnsureRegistrarSession();
        ViewId = id;
        Query = q?.Trim() ?? "";
        ProgramFilter = program?.Trim() ?? "";
        StatusFilter = status?.Trim() ?? "";

        DemoMode = ViewId == 0 && string.IsNullOrEmpty(Query) && string.IsNullOrEmpty(ProgramFilter) && string.IsNullOrEmpty(StatusFilter);
        var root = _store.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().Where(item => item["role"]?.GetValue<string>() == "student").ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var documents = CanonicalJsonStore.GetArray(root, "document_credentials").OfType<JsonObject>().ToList();
        ProgramsList = profiles.Select(item => JsonString(item, "program")).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).ToList();

        Students = users.Select(user =>
        {
            var studentId = JsonInt(user, "id");
            var profile = profiles.FirstOrDefault(item => JsonInt(item, "user_id") == studentId);
            var studentDocs = documents.Where(item => JsonInt(item, "student_id") == studentId).ToList();
            return new StudentListItem
            {
                Id = studentId,
                StudentIdNumber = JsonString(user, "student_id_number"),
                Name = JsonString(user, "name"),
                Email = JsonString(user, "email"),
                Program = JsonString(profile, "program"),
                YearLevel = JsonString(profile, "year_level"),
                EnrollmentStatus = JsonString(profile, "enrollment_status"),
                AcademicStatus = JsonString(profile, "academic_status"),
                AverageGrade = profile != null && profile["average_grade"] != null ? profile["average_grade"]!.GetValue<decimal>() : null,
                VerifiedCount = studentDocs.Count(item => JsonString(item, "status").Equals("Verified", StringComparison.OrdinalIgnoreCase)),
                TotalDocs = studentDocs.Count
            };
        }).Where(item => (string.IsNullOrEmpty(Query) || item.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) || item.StudentIdNumber.Contains(Query, StringComparison.OrdinalIgnoreCase) || item.Email.Contains(Query, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(ProgramFilter) || item.Program == ProgramFilter)
            && (string.IsNullOrEmpty(StatusFilter) || item.EnrollmentStatus == StatusFilter))
          .OrderBy(item => item.Name).ToList();

        LoadStudents();
        if (ViewId > 0)
        {
            var user = users.FirstOrDefault(item => JsonInt(item, "id") == ViewId);
            if (user == null) return RedirectToPage("/StudentRecords");
            Student = new AppUser
            {
                Id = ViewId,
                StudentIdNumber = JsonString(user, "student_id_number"),
                Name = JsonString(user, "name"),
                Email = JsonString(user, "email"),
                Role = "student",
                CreatedAt = JsonDate(user, "created_at", DateTime.UtcNow)
            };

            var profile = profiles.FirstOrDefault(item => JsonInt(item, "user_id") == ViewId);
            if (profile != null)
            {
                Profile = new StudentProfileDetail
                {
                    Program = JsonString(profile, "program", "Not set"),
                    YearLevel = JsonString(profile, "year_level", "1st Year"),
                    AverageGrade = profile["average_grade"]?.GetValue<decimal?>(),
                    AcademicStatus = JsonString(profile, "academic_status", "Good Standing"),
                    EnrollmentStatus = JsonString(profile, "enrollment_status", "Active"),
                    CompletedUnits = JsonInt(profile, "completed_units"),
                    UnitsRemaining = JsonInt(profile, "units_remaining", 144),
                    GuidanceClearanceStatus = JsonString(profile, "guidance_clearance_status", "Cleared"),
                    ContactNumber = JsonString(profile, "contact_number"),
                    Address = JsonString(profile, "address"),
                    Gender = JsonString(profile, "gender", "Unspecified")
                };
            }

            Vault = documents.Where(item => JsonInt(item, "student_id") == ViewId).OrderBy(item => JsonInt(item, "id"))
                .Select(item => new DocumentCredential
                {
                    Id = JsonInt(item, "id"),
                    StudentId = ViewId,
                    DocumentType = JsonString(item, "document_name", JsonString(item, "document_type")),
                    Status = JsonString(item, "status", "Missing"),
                    Remarks = JsonString(item, "remarks"),
                    FilePath = JsonString(item, "file_path"),
                    SubmittedAt = JsonNullableDate(item, "submission_date", "submitted_at"),
                    VerifiedAt = JsonNullableDate(item, "verified_at")
                }).ToList();

            var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().Where(item => JsonInt(item, "student_id") == ViewId).ToList();
            var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().Where(item => JsonInt(item, "student_id") == ViewId).ToList();
            var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();
            var offerings = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().ToList();
            StudentGrades = enrolledSubjects.Select(subject =>
            {
                var enrollment = enrollments.FirstOrDefault(item => JsonInt(item, "id") == JsonInt(subject, "enrollment_id"));
                var offering = offerings.FirstOrDefault(item => JsonInt(item, "id") == JsonInt(subject, "class_offering_id"));
                var grade = grades.FirstOrDefault(item => JsonInt(item, "enrolled_subject_id") == JsonInt(subject, "id"));
                return new StudentGradeRecord
                {
                    SubjectCode = JsonString(subject, "subject_code"),
                    SubjectName = JsonString(subject, "subject_name"),
                    Units = JsonInt(subject, "units"),
                    SectionCode = JsonString(offering, "section_code"),
                    SchoolYear = JsonString(enrollment, "school_year"),
                    Semester = JsonString(enrollment, "semester"),
                    Grade = grade != null && grade["final_grade"] != null ? grade["final_grade"]!.GetValue<decimal>() : null,
                    IsInc = grade?["is_inc"]?.GetValue<bool>() == true,
                    GradeStatus = grade == null ? "Pending" : JsonString(grade, "status", "final"),
                    Remarks = JsonString(grade, "remarks")
                };
            }).OrderByDescending(item => item.SchoolYear).ThenBy(item => item.SubjectCode).ToList();

            var clearance = CanonicalJsonStore.GetArray(root, "student_clearance").OfType<JsonObject>().LastOrDefault(item =>
                JsonInt(item, "student_id") == ViewId && JsonString(item, "department_name").Contains("Finance", StringComparison.OrdinalIgnoreCase));
            FinanceClearanceStatus = JsonString(clearance, "status", "Cleared");
            TotalPaidAmount = CanonicalJsonStore.GetArray(root, "student_payments").OfType<JsonObject>().Where(item => JsonInt(item, "student_id") == ViewId).Sum(item => item["amount"]?.GetValue<decimal>() ?? 0m);
        }

        return Page();
    }

    public IActionResult OnPostCreateStudent(string? student_id_number, string? name, string? email, string? program, string? year_level, string? gender, string? contact_number)
        => OnPostCreate(student_id_number, name, email, program, year_level, gender, contact_number);

    public IActionResult OnPostCreate(string? studentIdNumber, string? name, string? email, string? program, string? yearLevel, string? gender = null, string? contactNumber = null)
    {
        DemoMode = true;
        var inputError = StudentInput.Validate(studentIdNumber, name, email, program, yearLevel, gender, contactNumber, out var input);
        if (inputError is not null)
        {
            Error = inputError;
            LoadStudents();
            return Page();
        }

        try
        {
            var normalizedEmail = input.Email;
            var normalizedId = input.StudentIdNumber;
            var password = CreateTemporaryPassword();
            var createdId = 0;

            _store.UpdateRoot(root =>
            {
                var users = CanonicalJsonStore.GetOrCreateArray(root, "users");
                if (StudentInput.HasMatch(users.OfType<JsonObject>().Select(item => item["student_id_number"]?.GetValue<string>()), normalizedId) ||
                    StudentInput.HasMatch(users.OfType<JsonObject>().Select(item => item["email"]?.GetValue<string>()), normalizedEmail))
                    throw new InvalidOperationException("A student with that ID number or email already exists.");

                createdId = users.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
                var actualId = StudentInput.ResolveStudentId(normalizedId, createdId);
                var now = DateTime.UtcNow.ToString("O");
                users.Add(new JsonObject
                {
                    ["id"] = createdId,
                    ["student_id_number"] = actualId,
                    ["name"] = input.Name,
                    ["email"] = normalizedEmail,
                    ["role"] = "student",
                    ["status"] = "pending",
                    ["account_state"] = "pending_payment",
                    ["password_hash"] = BCrypt.Net.BCrypt.HashPassword(password),
                    ["created_at"] = now
                });

                CanonicalJsonStore.GetOrCreateArray(root, "student_profiles").Add(new JsonObject
                {
                    ["user_id"] = createdId,
                    ["program"] = input.Program,
                    ["year_level"] = input.YearLevel,
                    ["curriculum_year"] = "2026-2030",
                    ["academic_status"] = "Good Standing",
                    ["enrollment_status"] = "Pending Payment",
                    ["completed_units"] = 0,
                    ["units_remaining"] = 144,
                    ["guidance_clearance_status"] = "Pending",
                    ["guidance_notes"] = "Applicant awaiting payment",
                    ["gender"] = string.IsNullOrEmpty(input.Gender) ? "Unspecified" : input.Gender,
                    ["contact_number"] = input.ContactNumber
                });

                var enrollmentId = NextId(root, "enrollments");
                CanonicalJsonStore.GetOrCreateArray(root, "enrollments").Add(new JsonObject
                {
                    ["id"] = enrollmentId,
                    ["student_id"] = createdId,
                    ["school_year"] = "2026-2027",
                    ["semester"] = "1st Semester",
                    ["total_units"] = 0,
                    ["status"] = "pending_payment",
                    ["created_at"] = now
                });

                CanonicalJsonStore.GetOrCreateArray(root, "fee_assessments").Add(new JsonObject
                {
                    ["id"] = NextId(root, "fee_assessments"),
                    ["enrollment_id"] = enrollmentId,
                    ["student_id"] = createdId,
                    ["total_net_assessment"] = 10000.0,
                    ["total_paid"] = 0.0,
                    ["remaining_balance"] = 10000.0,
                    ["status"] = "Pending"
                });

                var documents = CanonicalJsonStore.GetOrCreateArray(root, "document_credentials");
                foreach (var documentType in VaultDocumentTypes)
                    documents.Add(new JsonObject { ["id"] = NextId(root, "document_credentials"), ["student_id"] = createdId, ["document_type"] = documentType, ["status"] = "Missing" });
            });

            TemporaryPassword = password;
            Message = "Applicant created. Record the temporary password, then record payment before registration.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student Records JSON creation failed");
            Error = ex.Message;
        }

        LoadStudents();
        return Page();
    }

    public IActionResult OnPostRecordPayment(string? studentIdNumber, decimal amount)
    {
        DemoMode = true;
        if (string.IsNullOrWhiteSpace(studentIdNumber))
        {
            Error = "Choose a student first.";
            LoadStudents();
            return Page();
        }

        try
        {
            _store.UpdateRoot(root =>
            {
                var user = FindUser(root, studentIdNumber);
                var studentId = user["id"]!.GetValue<int>();
                var enrollment = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().FirstOrDefault(item => item["student_id"]?.GetValue<int>() == studentId)
                    ?? throw new InvalidOperationException("No pending enrollment exists for this student.");
                if (CanonicalJsonStore.GetOrCreateArray(root, "student_payments").OfType<JsonObject>().Any(item => item["student_id"]?.GetValue<int>() == studentId && item["status"]?.GetValue<string>() == "Paid"))
                    throw new InvalidOperationException("A paid payment already exists for this student.");
                if (amount < 10000) throw new InvalidOperationException("The Registrar demo requires a ₱10,000 down payment.");

                var assessment = CanonicalJsonStore.GetArray(root, "fee_assessments").OfType<JsonObject>().FirstOrDefault(item => item["student_id"]?.GetValue<int>() == studentId)
                    ?? throw new InvalidOperationException("No assessment exists for this student.");
                assessment["total_paid"] = amount;
                assessment["remaining_balance"] = Math.Max(0, 10000 - amount);
                assessment["status"] = "Paid";
                CanonicalJsonStore.GetOrCreateArray(root, "student_payments").Add(new JsonObject
                {
                    ["id"] = NextId(root, "student_payments"),
                    ["student_id"] = studentId,
                    ["assessment_id"] = assessment["id"]!.GetValue<int>(),
                    ["receipt_no"] = $"REG-OR-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["amount"] = amount,
                    ["pay_date"] = DateTime.UtcNow.ToString("O"),
                    ["payment_method"] = "Registrar Demo",
                    ["payment_category"] = "Downpayment",
                    ["status"] = "Paid"
                });
                user["status"] = "active";
                user["account_state"] = "account_created";
                enrollment["status"] = "paid";
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().First(item => item["user_id"]?.GetValue<int>() == studentId);
                profile["enrollment_status"] = "Paid";
            });
            Message = "Payment recorded. The account is active; register the student to create the demo COR.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student Records JSON payment failed");
            Error = ex.Message;
        }
        LoadStudents();
        return Page();
    }

    public IActionResult OnPostRegister(string? studentIdNumber)
    {
        DemoMode = true;
        if (string.IsNullOrWhiteSpace(studentIdNumber))
        {
            Error = "Choose a student first.";
            LoadStudents();
            return Page();
        }

        try
        {
            _store.UpdateRoot(root =>
            {
                var user = FindUser(root, studentIdNumber);
                var studentId = user["id"]!.GetValue<int>();
                if (!CanonicalJsonStore.GetArray(root, "student_payments").OfType<JsonObject>().Any(item => item["student_id"]?.GetValue<int>() == studentId && item["status"]?.GetValue<string>() == "Paid"))
                    throw new InvalidOperationException("A paid payment is required before registration.");
                var enrollment = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().First(item => item["student_id"]?.GetValue<int>() == studentId);
                enrollment["status"] = "registered";
                enrollment["registered_at"] = DateTime.UtcNow.ToString("O");
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().First(item => item["user_id"]?.GetValue<int>() == studentId);
                profile["enrollment_status"] = "Active";
                var cors = CanonicalJsonStore.GetOrCreateArray(root, "certificate_of_registrations");
                if (!cors.OfType<JsonObject>().Any(item => item["student_id"]?.GetValue<int>() == studentId))
                    cors.Add(new JsonObject { ["id"] = NextId(root, "certificate_of_registrations"), ["student_id"] = studentId, ["school_year"] = enrollment["school_year"]!.GetValue<string>(), ["semester"] = enrollment["semester"]!.GetValue<string>(), ["total_units"] = enrollment["total_units"]!.GetValue<int>(), ["payment_status"] = "Paid", ["registration_date"] = DateTime.UtcNow.ToString("O"), ["status"] = "Demo COR" });
            });
            Message = "Student registered and demo COR created.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student Records JSON registration failed");
            Error = ex.Message;
        }
        LoadStudents();
        return Page();
    }

    private void LoadStudents()
    {
        DemoStudents.Clear();
        var users = CanonicalJsonStore.GetArray(_store.ReadRoot(), "users");
        foreach (var user in users.OfType<JsonObject>().Where(item => item["role"]?.GetValue<string>() == "student"))
            DemoStudents.Add(new DemoStudent(user["student_id_number"]?.GetValue<string>() ?? "", user["name"]?.GetValue<string>() ?? "", user["email"]?.GetValue<string>() ?? "", user["status"]?.GetValue<string>() ?? "", user["account_state"]?.GetValue<string>() ?? ""));
    }

    private static JsonObject FindUser(JsonObject root, string? studentIdNumber) =>
        StudentInput.FindUserByStudentId(CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>(), studentIdNumber);

    private static int NextId(JsonObject root, string collection) =>
        CanonicalJsonStore.GetOrCreateArray(root, collection).OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;

    private static string JsonString(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int JsonInt(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static DateTime JsonDate(JsonObject? item, string property, DateTime fallback) => DateTime.TryParse(JsonString(item, property), out var value) ? value : fallback;
    private static DateTime? JsonNullableDate(JsonObject item, string property, string? fallbackProperty = null)
    {
        var value = item[property]?.GetValue<string>() ?? (fallbackProperty == null ? null : item[fallbackProperty]?.GetValue<string>());
        return DateTime.TryParse(value, out var date) ? date : null;
    }

    private static string CreateTemporaryPassword() => $"NU-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6))}";

    public sealed record DemoStudent(string StudentIdNumber, string Name, string Email, string Status, string AccountState);

    public async Task<IActionResult> OnPostUpdateStudentStatusAsync(int student_id, string enrollment_status, string academic_status, string guidance_clearance_status)
    {
        _db.EnsureRegistrarSession();
        if (student_id > 0 && EnrollmentStatuses.Contains(enrollment_status) && AcademicStatuses.Contains(academic_status))
        {
            _store.UpdateRoot(root =>
            {
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => JsonInt(item, "user_id") == student_id)
                    ?? throw new InvalidOperationException("Student profile was not found.");
                profile["enrollment_status"] = enrollment_status;
                profile["academic_status"] = academic_status;
                profile["guidance_clearance_status"] = guidance_clearance_status;
            });

            await _db.LogActivityAsync(student_id, $"Student status updated: Enr={enrollment_status}, Acad={academic_status}, Guidance={guidance_clearance_status} by {_db.CurrentUserName}");
            TempData["FlashMessage"] = "Student status updated.";
            TempData["FlashType"] = "success";
        }
        return RedirectToPage("/StudentRecords", new { id = student_id });
    }

    public async Task<IActionResult> OnPostUpdateDocumentAsync(int student_id, int doc_id, string doc_status, string? remarks)
    {
        _db.EnsureRegistrarSession();
        if (student_id > 0 && doc_id > 0 && new[] { "Missing", "Submitted", "Verified" }.Contains(doc_status))
        {
            var r = (remarks ?? "").Trim();
            _store.UpdateRoot(root =>
            {
                var document = CanonicalJsonStore.GetArray(root, "document_credentials").OfType<JsonObject>().FirstOrDefault(item =>
                    JsonInt(item, "id") == doc_id && JsonInt(item, "student_id") == student_id)
                    ?? throw new InvalidOperationException("Document checklist item was not found.");
                var now = DateTime.UtcNow.ToString("O");
                document["status"] = doc_status;
                document["remarks"] = r;
                if (doc_status == "Missing")
                {
                    document["submission_date"] = null;
                    document["submitted_at"] = null;
                    document["verified_at"] = null;
                }
                else if (doc_status == "Submitted")
                {
                    document["submission_date"] ??= now;
                    document["submitted_at"] ??= now;
                    document["verified_at"] = null;
                }
                else
                {
                    document["submission_date"] ??= now;
                    document["submitted_at"] ??= now;
                    document["verified_at"] = now;
                }
            });

            await _db.LogActivityAsync(student_id, $"Document credential status updated to \"{doc_status}\" by {_db.CurrentUserName}");
            TempData["FlashMessage"] = "Document record updated.";
            TempData["FlashType"] = "success";
        }

        return RedirectToPage("/StudentRecords", new { id = student_id });
    }
}
