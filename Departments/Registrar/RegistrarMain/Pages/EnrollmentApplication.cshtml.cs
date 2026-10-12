using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class EnrollmentApplicationModel : PageModel
{
    private readonly CanonicalJsonStore _store;
    private readonly IWebHostEnvironment _environment;

    public EnrollmentApplicationModel(CanonicalJsonStore store, IWebHostEnvironment environment)
    {
        _store = store;
        _environment = environment;
    }

    public string SchoolYear { get; set; } = "2025-2026";
    public string Semester { get; set; } = "1st Semester";
    public List<string> Programs { get; set; } = new();
    public List<string> YearLevels { get; set; } = [.. StudentInput.YearLevels];
    public string OfferingsByYearJson { get; set; } = "{}";
    public string CurriculumMapJson { get; set; } = "{}";
    public ApplicationResult? Success { get; set; }
    public List<string> Errors { get; set; } = new();

    private static readonly string[] DocumentTypes =
    [
        "Form 137",
        "Form 138",
        "Birth Certificate",
        "Good Moral",
        "Transcript from Previous School",
        "Medical Clearance"
    ];

    private static readonly Dictionary<string, string> CurriculumMap = new()
    {
        ["BS Information Technology"] = "BSIT"
    };

    public void OnGet() => LoadDropdowns();

    public async Task<IActionResult> OnPostAsync(
        string enrollment_type,
        string full_name,
        string email,
        string contact_number,
        string gender,
        string birth_date,
        string address,
        string program,
        string year_level,
        string? previous_school,
        [FromForm(Name = "class_offering_ids[]")] List<int>? class_offering_ids)
    {
        LoadDropdowns();

        var enrollmentType = enrollment_type is "new" or "transferee" ? enrollment_type : "new";
        if (!new[] { "Male", "Female" }.Contains(gender)) gender = "Female";
        if (!YearLevels.Contains(year_level)) year_level = "1st Year";
        if (!Programs.Contains(program)) Errors.Add("Please select a valid degree program.");
        if (string.IsNullOrWhiteSpace(full_name)) Errors.Add("Full name is required.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) Errors.Add("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(contact_number)) Errors.Add("Contact number is required.");
        if (string.IsNullOrWhiteSpace(birth_date) || !DateOnly.TryParse(birth_date, out _)) Errors.Add("Date of birth is required.");
        if (string.IsNullOrWhiteSpace(address)) Errors.Add("Home address is required.");
        if (enrollmentType == "transferee" && string.IsNullOrWhiteSpace(previous_school)) Errors.Add("Please indicate your previous school for a transferee application.");

        var initialRoot = _store.ReadRoot();
        var initialUsers = CanonicalJsonStore.GetArray(initialRoot, "users").OfType<JsonObject>().ToList();
        if (initialUsers.Any(user => string.Equals(JsonString(user, "email"), email?.Trim(), StringComparison.OrdinalIgnoreCase)))
            Errors.Add("An account with that email address already exists. Please contact the Registrar's Office if this is a mistake.");

        var selectedIds = (class_offering_ids ?? new List<int>()).Distinct().ToList();
        var selectedOfferings = new List<SelectedOffering>();
        if (Errors.Count == 0 && selectedIds.Count > 0)
        {
            var offerings = CanonicalJsonStore.GetArray(initialRoot, "class_offerings").OfType<JsonObject>().ToList();
            var subjects = CanonicalJsonStore.GetArray(initialRoot, "subjects").OfType<JsonObject>().ToList();
            foreach (var id in selectedIds)
            {
                var offering = offerings.FirstOrDefault(item => JsonInt(item, "id") == id && JsonString(item, "school_year") == SchoolYear && JsonString(item, "semester") == Semester);
                var subject = offering == null ? null : subjects.FirstOrDefault(item => JsonInt(item, "id") == JsonInt(offering, "subject_id"));
                var capacity = offering == null ? 0 : JsonInt(offering, "max_capacity");
                var enrolled = offering == null ? 0 : JsonInt(offering, "enrolled_count");
                if (offering == null || subject == null || JsonString(offering, "status", "open") != "open" || (capacity > 0 && enrolled >= capacity))
                {
                    Errors.Add("One or more selected classes are no longer available. Review your class selections and try again.");
                    break;
                }
                selectedOfferings.Add(new SelectedOffering(offering, subject));
            }
        }

        var documentUploads = new Dictionary<string, string>
        {
            ["doc_Form137"] = "Form 137",
            ["doc_Form138"] = "Form 138",
            ["doc_BirthCertificate"] = "Birth Certificate",
            ["doc_GoodMoral"] = "Good Moral",
            ["doc_Transcript"] = "Transcript from Previous School",
            ["doc_MedicalClearance"] = "Medical Clearance"
        };
        foreach (var (fieldName, documentType) in documentUploads)
        {
            var file = Request.Form.Files[fieldName];
            if (file is { Length: > 5_242_880 }) Errors.Add($"{documentType} upload must be 5 MB or smaller.");
            if (file is { Length: > 0 } && !new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                Errors.Add($"{documentType} must be a PDF or image file.");
        }
        if (Errors.Count > 0) return Page();

        try
        {
            var yearLevelIndex = YearLevels.IndexOf(year_level);
            var entryYear = int.Parse(SchoolYear[..4]) - yearLevelIndex;
            var nextUserId = initialUsers.Select(user => JsonInt(user, "id")).DefaultIfEmpty(0).Max() + 1;
            var studentIdNumber = GenerateStudentId(initialUsers, entryYear);
            var tempPassword = $"NU-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            var curriculumYear = $"{entryYear}-{entryYear + 4}";
            var guidanceNote = enrollmentType == "transferee"
                ? $"Transferee applicant from {previous_school}; awaiting initial guidance interview."
                : "New applicant; awaiting initial guidance interview.";
            var totalUnits = selectedOfferings.Sum(item => JsonDecimal(item.Subject, "units"));
            var uploadsDirectory = Path.Combine(_environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "documents", nextUserId.ToString());
            var uploadedPaths = new Dictionary<string, string>();

            foreach (var (fieldName, documentType) in documentUploads)
            {
                var file = Request.Form.Files[fieldName];
                if (file is not { Length: > 0 }) continue;
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var fileName = $"{documentType.Replace(" ", "_")}_{Guid.NewGuid():N}{extension}";
                Directory.CreateDirectory(uploadsDirectory);
                using var stream = System.IO.File.Create(Path.Combine(uploadsDirectory, fileName));
                await file.CopyToAsync(stream);
                uploadedPaths[documentType] = $"uploads/documents/{nextUserId}/{fileName}";
            }

            var chosenSubjects = selectedOfferings.Select(item => JsonString(item.Subject, "subject_code")).ToList();
            _store.UpdateRoot(root =>
            {
                var users = CanonicalJsonStore.GetOrCreateArray(root, "users");
                if (users.OfType<JsonObject>().Any(user => string.Equals(JsonString(user, "email"), email.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("An account with that email address already exists.");
                if (users.OfType<JsonObject>().Select(user => JsonInt(user, "id")).DefaultIfEmpty(0).Max() + 1 != nextUserId)
                    throw new InvalidOperationException("The student records changed while the application was being submitted. Please try again.");

                var now = DateTime.UtcNow.ToString("O");
                users.Add(new JsonObject
                {
                    ["id"] = nextUserId,
                    ["student_id_number"] = studentIdNumber,
                    ["name"] = full_name.Trim(),
                    ["email"] = email.Trim(),
                    ["role"] = "student",
                    ["status"] = "pending",
                    ["account_state"] = "pending_payment",
                    ["password_hash"] = passwordHash,
                    ["created_at"] = now
                });

                CanonicalJsonStore.GetOrCreateArray(root, "student_profiles").Add(new JsonObject
                {
                    ["user_id"] = nextUserId,
                    ["program"] = program,
                    ["year_level"] = year_level,
                    ["curriculum_year"] = curriculumYear,
                    ["academic_status"] = "Good Standing",
                    ["enrollment_status"] = "Pending Payment",
                    ["completed_units"] = 0,
                    ["units_remaining"] = 144,
                    ["guidance_clearance_status"] = "Pending",
                    ["guidance_notes"] = guidanceNote,
                    ["contact_number"] = contact_number.Trim(),
                    ["address"] = address.Trim(),
                    ["birth_date"] = birth_date,
                    ["gender"] = gender,
                    ["previous_school"] = previous_school
                });

                var enrollments = CanonicalJsonStore.GetOrCreateArray(root, "enrollments");
                var enrollmentId = NextId(enrollments);
                enrollments.Add(new JsonObject
                {
                    ["id"] = enrollmentId,
                    ["student_id"] = nextUserId,
                    ["school_year"] = SchoolYear,
                    ["semester"] = Semester,
                    ["enrollment_type"] = enrollmentType,
                    ["status"] = "pending_payment",
                    ["total_units"] = totalUnits,
                    ["created_at"] = now
                });

                var assessments = CanonicalJsonStore.GetOrCreateArray(root, "fee_assessments");
                assessments.Add(new JsonObject
                {
                    ["id"] = NextId(assessments),
                    ["enrollment_id"] = enrollmentId,
                    ["student_id"] = nextUserId,
                    ["total_net_assessment"] = 10000m,
                    ["total_paid"] = 0m,
                    ["remaining_balance"] = 10000m,
                    ["status"] = "Pending"
                });

                var enrolledSubjects = CanonicalJsonStore.GetOrCreateArray(root, "enrolled_subjects");
                var offerings = CanonicalJsonStore.GetArray(root, "class_offerings");
                foreach (var selection in selectedOfferings)
                {
                    var offeringId = JsonInt(selection.Offering, "id");
                    var offering = offerings.OfType<JsonObject>().First(item => JsonInt(item, "id") == offeringId);
                    var enrolledCount = JsonInt(offering, "enrolled_count");
                    var capacity = JsonInt(offering, "max_capacity");
                    if (capacity > 0 && enrolledCount >= capacity) throw new InvalidOperationException("A selected class filled while the application was being submitted.");
                    enrolledSubjects.Add(new JsonObject
                    {
                        ["id"] = NextId(enrolledSubjects),
                        ["enrollment_id"] = enrollmentId,
                        ["student_id"] = nextUserId,
                        ["class_offering_id"] = offeringId,
                        ["subject_code"] = JsonString(selection.Subject, "subject_code"),
                        ["subject_name"] = JsonString(selection.Subject, "subject_name"),
                        ["units"] = JsonDecimal(selection.Subject, "units"),
                        ["status"] = "enrolled"
                    });
                    offering["enrolled_count"] = enrolledCount + 1;
                }

                var documents = CanonicalJsonStore.GetOrCreateArray(root, "document_credentials");
                foreach (var documentType in DocumentTypes)
                {
                    var document = new JsonObject
                    {
                        ["id"] = NextId(documents),
                        ["student_id"] = nextUserId,
                        ["document_type"] = documentType,
                        ["status"] = uploadedPaths.ContainsKey(documentType) ? "Submitted" : "Missing",
                        ["remarks"] = documentType == "Transcript from Previous School" && enrollmentType == "transferee" ? $"Awaiting official TOR from {previous_school}." : null
                    };
                    if (uploadedPaths.TryGetValue(documentType, out var filePath))
                    {
                        document["file_path"] = filePath;
                        document["submission_date"] = now;
                    }
                    documents.Add(document);
                }

                AppendApplicationActivity(root, nextUserId, $"Enrollment application submitted: {full_name} ({studentIdNumber}); pending payment.");
            });

            Success = new ApplicationResult
            {
                StudentIdNumber = studentIdNumber,
                PortalEmail = email,
                TempPassword = tempPassword,
                Program = program,
                YearLevel = year_level,
                EnrollmentType = enrollmentType,
                TotalUnits = (float)totalUnits,
                Subjects = chosenSubjects,
                SchoolYear = SchoolYear,
                Semester = Semester
            };
        }
        catch (Exception ex)
        {
            Errors.Add("We could not process your application: " + ex.Message);
        }

        return Page();
    }

    private void LoadDropdowns()
    {
        var root = _store.ReadRoot();
        var settings = CanonicalJsonStore.GetArray(root, "settings").OfType<JsonObject>().ToList();
        SchoolYear = settings.FirstOrDefault(item => JsonString(item, "key") == "current_school_year") is { } yearSetting ? JsonString(yearSetting, "value", "2025-2026") : "2025-2026";
        Semester = settings.FirstOrDefault(item => JsonString(item, "key") == "current_semester") is { } semesterSetting ? JsonString(semesterSetting, "value", "1st Semester") : "1st Semester";
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>();
        Programs = StudentInput.Programs.Union(profiles.Select(item => JsonString(item, "program")).Where(item => !string.IsNullOrWhiteSpace(item))).Distinct().OrderBy(item => item).ToList();

        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
        var grouped = new Dictionary<string, List<object>>();
        foreach (var offering in CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().Where(item => JsonString(item, "school_year") == SchoolYear && JsonString(item, "semester") == Semester))
        {
            var subject = subjects.FirstOrDefault(item => JsonInt(item, "id") == JsonInt(offering, "subject_id"));
            if (subject == null) continue;
            var yearLevel = JsonString(subject, "year_level", "1st Year");
            if (!grouped.ContainsKey(yearLevel)) grouped[yearLevel] = new List<object>();
            grouped[yearLevel].Add(new
            {
                id = JsonInt(offering, "id"),
                code = JsonString(subject, "subject_code"),
                name = JsonString(subject, "subject_name"),
                units = JsonDecimal(subject, "units"),
                section = JsonString(offering, "section_code"),
                room = JsonString(offering, "room"),
                schedule = $"{JsonString(offering, "days_of_week")} {FormatTime(offering, "start_time")}-{FormatTime(offering, "end_time")}",
                instructor = JsonString(offering, "instructor_name"),
                capacity = JsonInt(offering, "max_capacity"),
                taken = JsonInt(offering, "enrolled_count"),
                status = JsonString(offering, "status", "open")
            });
        }

        OfferingsByYearJson = JsonSerializer.Serialize(grouped);
        CurriculumMapJson = JsonSerializer.Serialize(CurriculumMap);
    }

    private string GenerateStudentId(IEnumerable<JsonObject> users, int entryYear)
    {
        var prefix = entryYear + "-";
        var nextNumber = users.Select(user => JsonString(user, "student_id_number"))
            .Where(number => number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(number => int.TryParse(number[prefix.Length..], out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0).Max() + 1;
        return $"{entryYear}-{nextNumber:D5}";
    }

    private static int NextId(JsonArray rows) => rows.OfType<JsonObject>().Select(item => JsonInt(item, "id")).DefaultIfEmpty(0).Max() + 1;
    private static string JsonString(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int JsonInt(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static decimal JsonDecimal(JsonObject? item, string property) => item?[property]?.GetValue<decimal>() ?? 0m;
    private static string FormatTime(JsonObject offering, string property) =>
        TimeSpan.TryParse(JsonString(offering, property), out var time) ? DateTime.Today.Add(time).ToString("hh:mm tt") : "";

    private void AppendApplicationActivity(JsonObject root, int studentId, string message)
    {
        var activities = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
        activities.Add(new JsonObject
        {
            ["id"] = NextId(activities),
            ["user_id"] = studentId,
            ["user_name"] = "Student Applicant",
            ["message"] = message,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        });
    }

    private sealed record SelectedOffering(JsonObject Offering, JsonObject Subject);

    public class ApplicationResult
    {
        public string StudentIdNumber { get; set; } = "";
        public string PortalEmail { get; set; } = "";
        public string TempPassword { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string EnrollmentType { get; set; } = "";
        public float TotalUnits { get; set; }
        public List<string> Subjects { get; set; } = new();
        public string SchoolYear { get; set; } = "";
        public string Semester { get; set; } = "";
    }
}
