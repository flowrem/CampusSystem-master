using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using RegistrarMain.Models;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class EnrollmentValidationModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly CanonicalJsonStore _jsonStore;

    public EnrollmentValidationModel(DatabaseService db, CanonicalJsonStore jsonStore)
    {
        _db = db;
        _jsonStore = jsonStore;
    }

    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string CurrentSemester { get; set; } = "1st Semester";
    public string ActiveTab { get; set; } = "registration";

    public List<PendingEnrollmentItem> PendingList { get; set; } = new();
    public List<PendingAddDropItem> PendingAddDrop { get; set; } = new();
    public List<PendingOverloadItem> PendingOverloads { get; set; } = new();
    public List<ActiveEnrolleeItem> ActiveList { get; set; } = new();

    public class PendingEnrollmentItem
    {
        public int Id { get; set; }
        public string SchoolYear { get; set; } = "";
        public string Semester { get; set; } = "";
        public string EnrollmentType { get; set; } = "";
        public int TotalUnits { get; set; }
        public DateTime EnrolledAt { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string AcademicStatus { get; set; } = "";
        public int SubjectsCount { get; set; }
        public string FinanceClearanceStatus { get; set; } = "Cleared";
    }

    public class PendingAddDropItem
    {
        public int Id { get; set; }
        public string RequestType { get; set; } = "";
        public string? Reason { get; set; }
        public DateTime RequestedAt { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Program { get; set; } = "";
        public string? FromCode { get; set; }
        public string? FromName { get; set; }
        public string? ToCode { get; set; }
        public string? ToName { get; set; }
    }

    public class PendingOverloadItem
    {
        public int Id { get; set; }
        public string RequestType { get; set; } = "";
        public int RequestedUnits { get; set; }
        public string? Reason { get; set; }
        public DateTime RequestedAt { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public decimal? AverageGrade { get; set; }
    }

    public class ActiveEnrolleeItem
    {
        public int Id { get; set; }
        public string SchoolYear { get; set; } = "";
        public string Semester { get; set; } = "";
        public string EnrollmentType { get; set; } = "";
        public int TotalUnits { get; set; }
        public DateTime EnrolledAt { get; set; }
        public int StudentId { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
    }

    public IActionResult OnGet(string tab = "registration")
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "registration", "adddrop", "overload", "active_list" }.Contains(tab) ? tab : "registration";

        var root = _jsonStore.ReadRoot();
        var settings = CanonicalJsonStore.GetArray(root, "settings").OfType<JsonObject>();
        CurrentSchoolYear = settings.FirstOrDefault(item => StringValue(item, "key") == "current_school_year") is { } yearSetting
            ? StringValue(yearSetting, "value", "2025-2026") : "2025-2026";
        CurrentSemester = settings.FirstOrDefault(item => StringValue(item, "key") == "current_semester") is { } semesterSetting
            ? StringValue(semesterSetting, "value", "1st Semester") : "1st Semester";

        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var clearances = CanonicalJsonStore.GetArray(root, "student_clearance").OfType<JsonObject>().ToList();
        var offerings = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().ToList();
        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();

        PendingList = enrollments.Where(item => StringValue(item, "status") == "pending")
            .Select(enrollment =>
            {
                var studentId = IntValue(enrollment, "student_id");
                var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
                var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                if (user == null) return null;
                var finance = clearances.FirstOrDefault(item => IntValue(item, "student_id") == studentId && StringValue(item, "department_name").Contains("Finance", StringComparison.OrdinalIgnoreCase));
                return new PendingEnrollmentItem
                {
                    Id = IntValue(enrollment, "id"),
                    SchoolYear = StringValue(enrollment, "school_year"),
                    Semester = StringValue(enrollment, "semester"),
                    EnrollmentType = StringValue(enrollment, "enrollment_type", "Regular"),
                    TotalUnits = IntValue(enrollment, "total_units"),
                    EnrolledAt = DateValue(enrollment, "enrolled_at", DateValue(enrollment, "created_at", DateTime.UtcNow)),
                    Name = StringValue(user, "name"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Email = StringValue(user, "email"),
                    Program = StringValue(profile, "program"),
                    YearLevel = StringValue(profile, "year_level"),
                    AcademicStatus = StringValue(profile, "academic_status", "Good Standing"),
                    SubjectsCount = enrolledSubjects.Count(item => IntValue(item, "enrollment_id") == IntValue(enrollment, "id")),
                    FinanceClearanceStatus = StringValue(finance, "status", "Cleared")
                };
            }).Where(item => item != null).Cast<PendingEnrollmentItem>().ToList();

        PendingAddDrop = CanonicalJsonStore.GetArray(root, "add_drop_requests").OfType<JsonObject>()
            .Where(item => StringValue(item, "status") == "pending")
            .Select(request =>
            {
                var studentId = IntValue(request, "student_id");
                var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
                var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                var fromOffering = offerings.FirstOrDefault(item => IntValue(item, "id") == IntValue(request, "class_offering_id"));
                var toOffering = offerings.FirstOrDefault(item => IntValue(item, "id") == IntValue(request, "target_class_offering_id"));
                var fromSubject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(fromOffering, "subject_id"));
                var toSubject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(toOffering, "subject_id"));
                if (user == null) return null;
                return new PendingAddDropItem
                {
                    Id = IntValue(request, "id"),
                    RequestType = StringValue(request, "request_type"),
                    Reason = StringValue(request, "reason"),
                    RequestedAt = DateValue(request, "requested_at", DateTime.UtcNow),
                    Name = StringValue(user, "name"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Program = StringValue(profile, "program"),
                    FromCode = StringValue(fromSubject, "subject_code"),
                    FromName = StringValue(fromSubject, "subject_name"),
                    ToCode = StringValue(toSubject, "subject_code"),
                    ToName = StringValue(toSubject, "subject_name")
                };
            }).Where(item => item != null).Cast<PendingAddDropItem>().ToList();

        PendingOverloads = CanonicalJsonStore.GetArray(root, "overload_waiver_requests").OfType<JsonObject>()
            .Where(item => StringValue(item, "status") == "pending")
            .Select(request =>
            {
                var studentId = IntValue(request, "student_id");
                var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
                var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                if (user == null) return null;
                return new PendingOverloadItem
                {
                    Id = IntValue(request, "id"),
                    RequestType = StringValue(request, "request_type"),
                    RequestedUnits = IntValue(request, "requested_units"),
                    Reason = StringValue(request, "reason"),
                    RequestedAt = DateValue(request, "requested_at", DateTime.UtcNow),
                    Name = StringValue(user, "name"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Program = StringValue(profile, "program"),
                    YearLevel = StringValue(profile, "year_level"),
                    AverageGrade = profile != null && profile["average_grade"] != null ? profile["average_grade"]!.GetValue<decimal>() : null
                };
            }).Where(item => item != null).Cast<PendingOverloadItem>().ToList();

        ActiveList = enrollments.Where(item =>
                new[] { "active", "registered", "enrolled" }.Contains(StringValue(item, "status"), StringComparer.OrdinalIgnoreCase) &&
                StringValue(item, "school_year") == CurrentSchoolYear && StringValue(item, "semester") == CurrentSemester)
            .Select(enrollment =>
            {
                var studentId = IntValue(enrollment, "student_id");
                var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
                var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                if (user == null) return null;
                return new ActiveEnrolleeItem
                {
                    Id = IntValue(enrollment, "id"),
                    SchoolYear = StringValue(enrollment, "school_year"),
                    Semester = StringValue(enrollment, "semester"),
                    EnrollmentType = StringValue(enrollment, "enrollment_type", "Regular"),
                    TotalUnits = IntValue(enrollment, "total_units"),
                    EnrolledAt = DateValue(enrollment, "enrolled_at", DateValue(enrollment, "created_at", DateTime.UtcNow)),
                    StudentId = studentId,
                    Name = StringValue(user, "name"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Program = StringValue(profile, "program"),
                    YearLevel = StringValue(profile, "year_level")
                };
            }).Where(item => item != null).Cast<ActiveEnrolleeItem>().ToList();

        return Page();
    }

    private static string StringValue(JsonObject? item, string property, string fallback = "") =>
        item?[property]?.GetValue<string>() ?? fallback;

    private static int IntValue(JsonObject? item, string property) =>
        item?[property]?.GetValue<int>() ?? 0;

    private static DateTime DateValue(JsonObject? item, string property, DateTime fallback) =>
        DateTime.TryParse(StringValue(item, property), out var value) ? value : fallback;

    private void AppendActivity(JsonObject root, int? userId, string message)
    {
        var activities = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
        var nextId = activities.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
        activities.Add(new JsonObject
        {
            ["id"] = nextId,
            ["user_id"] = userId,
            ["user_name"] = _db.CurrentUserName,
            ["message"] = message,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        });
    }

    public IActionResult OnPostEnrollmentAction(int enrollment_id, string action)
    {
        _db.EnsureRegistrarSession();
        if (action is not ("approve_enrollment" or "reject_enrollment"))
            return RedirectToPage("/EnrollmentValidation", new { tab = "registration" });

        var approved = action == "approve_enrollment";
        var message = "That enrollment record is no longer pending.";
        var success = false;
        _jsonStore.UpdateRoot(root =>
        {
            var enrollment = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().FirstOrDefault(item =>
                IntValue(item, "id") == enrollment_id && StringValue(item, "status") == "pending");
            if (enrollment == null) return;

            var studentId = IntValue(enrollment, "student_id");
            var user = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == studentId);
            if (user == null) return;

            if (approved)
            {
                if (!string.Equals(StringValue(user, "status"), "active", StringComparison.OrdinalIgnoreCase))
                {
                    message = "This student's account is not active. Record payment in Student Records before validating enrollment.";
                    return;
                }
                var isPaid = CanonicalJsonStore.GetArray(root, "student_payments").OfType<JsonObject>().Any(payment =>
                    IntValue(payment, "student_id") == studentId && string.Equals(StringValue(payment, "status"), "Paid", StringComparison.OrdinalIgnoreCase));
                if (!isPaid)
                {
                    message = "A paid payment record is required before enrollment can be validated.";
                    return;
                }

                enrollment["status"] = "registered";
                enrollment["registered_at"] = DateTime.UtcNow.ToString("O");
                enrollment["validated_by_registrar_id"] = _db.CurrentUserId;
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                if (profile != null) profile["enrollment_status"] = "Active";

                var certificates = CanonicalJsonStore.GetOrCreateArray(root, "certificate_of_registrations");
                var schoolYear = StringValue(enrollment, "school_year");
                var semester = StringValue(enrollment, "semester");
                if (!certificates.OfType<JsonObject>().Any(item => IntValue(item, "student_id") == studentId && StringValue(item, "school_year") == schoolYear && StringValue(item, "semester") == semester))
                {
                    var nextId = certificates.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
                    certificates.Add(new JsonObject
                    {
                        ["id"] = nextId,
                        ["student_id"] = studentId,
                        ["school_year"] = schoolYear,
                        ["semester"] = semester,
                        ["total_units"] = IntValue(enrollment, "total_units"),
                        ["payment_status"] = "Paid",
                        ["registration_date"] = DateTime.UtcNow.ToString("O"),
                        ["status"] = "Demo COR"
                    });
                }
                message = $"Enrollment #{enrollment_id} approved and synchronized to Student Portal.";
            }
            else
            {
                enrollment["status"] = "rejected";
                message = $"Enrollment #{enrollment_id} rejected.";
            }

            AppendActivity(root, studentId, message);
            success = true;
        });

        TempData["FlashMessage"] = message;
        TempData["FlashType"] = success ? "success" : "error";
        return RedirectToPage("/EnrollmentValidation", new { tab = "registration" });
    }

    public IActionResult OnPostAddDropAction(int request_id, string action)
    {
        _db.EnsureRegistrarSession();
        var message = "That petition is no longer pending.";
        var success = false;
        try
        {
            _jsonStore.UpdateRoot(root =>
            {
                var requests = CanonicalJsonStore.GetArray(root, "add_drop_requests");
                var request = requests.OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == request_id && StringValue(item, "status") == "pending");
                if (request == null) return;

                var studentId = IntValue(request, "student_id");
                var requestType = StringValue(request, "request_type");
                var approved = action == "approve_adddrop";
                if (!approved && action != "reject_adddrop") return;

                if (approved && requestType is "drop" or "change")
                {
                    var classOfferingId = IntValue(request, "class_offering_id");
                    var enrolled = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().FirstOrDefault(item =>
                        IntValue(item, "student_id") == studentId &&
                        IntValue(item, "enrollment_id") == IntValue(request, "enrollment_id") &&
                        IntValue(item, "class_offering_id") == classOfferingId && StringValue(item, "status") == "enrolled");
                    if (enrolled == null) throw new InvalidOperationException("The requested enrolled subject was not found.");
                    enrolled["status"] = "dropped";
                    var sourceOffering = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == classOfferingId);
                    if (sourceOffering != null) sourceOffering["enrolled_count"] = Math.Max(0, IntValue(sourceOffering, "enrolled_count") - 1);
                }

                if (approved && requestType is "add" or "change")
                {
                    var targetId = IntValue(request, "target_class_offering_id");
                    var targetOffering = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == targetId);
                    if (targetOffering == null) throw new InvalidOperationException("The requested target class offering was not found.");
                    var enrolledCount = IntValue(targetOffering, "enrolled_count");
                    var capacity = IntValue(targetOffering, "max_capacity");
                    if (capacity > 0 && enrolledCount >= capacity) throw new InvalidOperationException("The target class offering is full.");

                    var subjectId = IntValue(targetOffering, "subject_id");
                    var subject = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == subjectId);
                    var enrolledSubjects = CanonicalJsonStore.GetOrCreateArray(root, "enrolled_subjects");
                    var nextId = enrolledSubjects.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
                    enrolledSubjects.Add(new JsonObject
                    {
                        ["id"] = nextId,
                        ["enrollment_id"] = IntValue(request, "enrollment_id"),
                        ["student_id"] = studentId,
                        ["class_offering_id"] = targetId,
                        ["subject_code"] = StringValue(subject, "subject_code"),
                        ["subject_name"] = StringValue(subject, "subject_name"),
                        ["units"] = IntValue(subject, "units"),
                        ["status"] = "enrolled"
                    });
                    targetOffering["enrolled_count"] = enrolledCount + 1;
                }

                FeeCalculator.Adjustment? feeChange = null;
                if (approved)
                    feeChange = FeeCalculator.Recalculate(root, IntValue(request, "enrollment_id"), $"{requestType} petition #{request_id} approved");

                request["status"] = approved ? "approved" : "rejected";
                request["processed_at"] = DateTime.UtcNow.ToString("O");
                request["processed_by"] = _db.CurrentUserId;
                message = approved
                    ? "Petition approved and class schedule updated." + (feeChange is null ? "" : $" Fees changed by ₱{feeChange.Delta:N2}; new total ₱{feeChange.NewTotal:N2}.")
                    : "Petition rejected.";
                AppendActivity(root, studentId, $"{requestType} subject petition {request["status"]} by registrar.");
                success = true;
            });
        }
        catch (Exception ex)
        {
            message = "Error processing petition: " + ex.Message;
        }

        TempData["FlashMessage"] = message;
        TempData["FlashType"] = success ? "success" : "error";
        return RedirectToPage("/EnrollmentValidation", new { tab = "adddrop" });
    }

    public IActionResult OnPostOverloadAction(int request_id, string action)
    {
        _db.EnsureRegistrarSession();
        var newStatus = action == "approve_overload" ? "approved" : "rejected";
        var message = "That petition is no longer pending.";
        var success = false;
        _jsonStore.UpdateRoot(root =>
        {
            var request = CanonicalJsonStore.GetArray(root, "overload_waiver_requests").OfType<JsonObject>().FirstOrDefault(item =>
                IntValue(item, "id") == request_id && StringValue(item, "status") == "pending");
            if (request == null) return;

            var studentId = IntValue(request, "student_id");
            var requestType = StringValue(request, "request_type");
            request["status"] = newStatus;
            request["processed_at"] = DateTime.UtcNow.ToString("O");
            request["processed_by"] = _db.CurrentUserId;
            AppendActivity(root, studentId, $"{requestType} petition {newStatus} by registrar.");
            message = $"{requestType} petition {newStatus}.";
            success = true;
        });

        TempData["FlashMessage"] = message;
        TempData["FlashType"] = success ? "success" : "error";

        return RedirectToPage("/EnrollmentValidation", new { tab = "overload" });
    }
}
