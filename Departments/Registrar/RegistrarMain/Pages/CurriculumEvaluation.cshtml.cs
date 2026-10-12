using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class CurriculumEvaluationModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly CanonicalJsonStore _store;

    public CurriculumEvaluationModel(DatabaseService db, CanonicalJsonStore store)
    {
        _db = db;
        _store = store;
    }

    public string ActiveTab { get; set; } = "audit";
    public List<StudentDropdownItem> StudentsList { get; set; } = new();
    public List<SubjectDropdownItem> SubjectsList { get; set; } = new();
    public int SelectedStudentId { get; set; }
    public SelectedStudentDetail? SelectedStudent { get; set; }
    public List<CurriculumProgressItem> CurriculumProgress { get; set; } = new();
    public AuditStats Stats { get; set; } = new();
    public List<CreditedRecordItem> CreditedRecords { get; set; } = new();
    public List<ShiftingRequestItem> ShiftingRequests { get; set; } = new();

    public class StudentDropdownItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
    }

    public class SubjectDropdownItem
    {
        public int Id { get; set; }
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
    }

    public class SelectedStudentDetail
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string? CurriculumYear { get; set; } = "2024-2025";
    }

    public class CurriculumProgressItem
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Units { get; set; }
        public string? Prereq { get; set; }
        public string YearLevel { get; set; } = "";
        public string Semester { get; set; } = "";
        public string Status { get; set; } = "";
        public string Mark { get; set; } = "";
        public string Badge { get; set; } = "";
    }

    public class AuditStats
    {
        public decimal Total { get; set; }
        public decimal Completed { get; set; }
        public decimal InProgress { get; set; }
        public decimal Deficient { get; set; }
    }

    public class CreditedRecordItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string PrevSchool { get; set; } = "";
        public string PrevSubjectCode { get; set; } = "";
        public string PrevSubjectTitle { get; set; } = "";
        public decimal PrevUnits { get; set; }
        public string PrevGrade { get; set; } = "";
        public string CreditedCode { get; set; } = "";
        public string CreditedName { get; set; } = "";
        public string EvaluatedBy { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class ShiftingRequestItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string FromProgram { get; set; } = "";
        public string ToProgram { get; set; } = "";
        public decimal? AverageGrade { get; set; }
        public string? Reason { get; set; }
        public string Status { get; set; } = "pending";
    }

    public IActionResult OnGet(string tab = "audit", int student_id = 0)
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "audit", "crediting", "shifting" }.Contains(tab) ? tab : "audit";
        var root = _store.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().Where(item => StringValue(item, "role") == "student").ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();
        var credited = root["unified_seed_data"]?["transferee_credited_subjects"] as JsonArray ?? new JsonArray();

        StudentsList = users.Select(user =>
        {
            var studentId = IntValue(user, "id");
            var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
            return new StudentDropdownItem
            {
                Id = studentId,
                StudentIdNumber = StringValue(user, "student_id_number"),
                Name = StringValue(user, "name"),
                Program = StringValue(profile, "program"),
                YearLevel = StringValue(profile, "year_level")
            };
        }).OrderBy(item => item.Name).ToList();
        SubjectsList = subjects.Select(item => new SubjectDropdownItem
        {
            Id = IntValue(item, "id"),
            SubjectCode = StringValue(item, "subject_code"),
            SubjectName = StringValue(item, "subject_name"),
            Units = DecimalValue(item, "units")
        }).OrderBy(item => item.SubjectCode).ToList();
        SelectedStudentId = student_id > 0 ? student_id : StudentsList.FirstOrDefault()?.Id ?? 0;

        if (SelectedStudentId > 0)
        {
            var user = users.FirstOrDefault(item => IntValue(item, "id") == SelectedStudentId);
            var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == SelectedStudentId);
            if (user != null && profile != null)
            {
                SelectedStudent = new SelectedStudentDetail
                {
                    Id = SelectedStudentId,
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Name = StringValue(user, "name"),
                    Email = StringValue(user, "email"),
                    Program = StringValue(profile, "program"),
                    YearLevel = StringValue(profile, "year_level"),
                    CurriculumYear = StringValue(profile, "curriculum_year")
                };

                var studentSubjects = enrolledSubjects.Where(item => IntValue(item, "student_id") == SelectedStudentId).ToList();
                foreach (var subject in subjects.OrderBy(item => StringValue(item, "year_level")).ThenBy(item => StringValue(item, "semester")).ThenBy(item => StringValue(item, "subject_code")))
                {
                    var subjectId = IntValue(subject, "id");
                    var code = StringValue(subject, "subject_code");
                    var creditedRow = credited.OfType<JsonObject>().FirstOrDefault(item =>
                        IntValue(item, "student_id") == SelectedStudentId && IntValue(item, "credited_to_subject_id") == subjectId && StringValue(item, "status") == "approved");
                    var enrollmentSubject = studentSubjects.FirstOrDefault(item => StringValue(item, "subject_code") == code);
                    var grade = enrollmentSubject == null ? null : grades.FirstOrDefault(item => IntValue(item, "enrolled_subject_id") == IntValue(enrollmentSubject, "id"));
                    var progress = new CurriculumProgressItem
                    {
                        Code = code,
                        Name = StringValue(subject, "subject_name"),
                        Units = DecimalValue(subject, "units"),
                        Prereq = ReadPrerequisite(subject),
                        YearLevel = StringValue(subject, "year_level"),
                        Semester = StringValue(subject, "semester")
                    };

                    if (creditedRow != null)
                    {
                        progress.Status = "Credited (Transferee)";
                        progress.Mark = StringValue(creditedRow, "prev_grade");
                        progress.Badge = "badge-approved";
                        Stats.Completed += progress.Units;
                    }
                    else if (grade != null && grade["final_grade"] != null && grade["final_grade"]!.GetValue<decimal>() <= 3m && grade["is_inc"]?.GetValue<bool>() != true)
                    {
                        progress.Status = "Passed";
                        progress.Mark = grade["final_grade"]!.GetValue<decimal>().ToString("F2");
                        progress.Badge = "badge-active";
                        Stats.Completed += progress.Units;
                    }
                    else if (grade?["is_inc"]?.GetValue<bool>() == true)
                    {
                        progress.Status = "Incomplete (INC)";
                        progress.Mark = "INC";
                        progress.Badge = "badge-inc";
                        Stats.InProgress += progress.Units;
                    }
                    else if (enrollmentSubject != null)
                    {
                        progress.Status = "Enrolled / In-Progress";
                        progress.Mark = "Enrolled";
                        progress.Badge = "badge-open";
                        Stats.InProgress += progress.Units;
                    }
                    else
                    {
                        progress.Status = "Deficient";
                        progress.Mark = "-";
                        progress.Badge = "badge-missing";
                        Stats.Deficient += progress.Units;
                    }

                    Stats.Total += progress.Units;
                    CurriculumProgress.Add(progress);
                }
            }
        }

        CreditedRecords = credited.OfType<JsonObject>().Select(record =>
        {
            var studentId = IntValue(record, "student_id");
            var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
            var subject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(record, "credited_to_subject_id"));
            if (user == null) return null;
            return new CreditedRecordItem
            {
                Id = IntValue(record, "id"),
                StudentId = studentId,
                StudentName = StringValue(user, "name"),
                StudentIdNumber = StringValue(user, "student_id_number"),
                PrevSchool = StringValue(record, "prev_school"),
                PrevSubjectCode = StringValue(record, "prev_subject_code"),
                PrevSubjectTitle = StringValue(record, "prev_subject_title"),
                PrevUnits = DecimalValue(record, "prev_units"),
                PrevGrade = StringValue(record, "prev_grade"),
                CreditedCode = StringValue(subject, "subject_code"),
                CreditedName = StringValue(subject, "subject_name"),
                EvaluatedBy = StringValue(record, "evaluated_by"),
                Status = StringValue(record, "status")
            };
        }).Where(item => item != null).Cast<CreditedRecordItem>().ToList();

        ShiftingRequests = (root["unified_seed_data"]?["course_shifting_requests"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(request =>
        {
            var studentId = IntValue(request, "student_id");
            var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
            var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
            if (user == null) return null;
            return new ShiftingRequestItem
            {
                Id = IntValue(request, "id"),
                StudentId = studentId,
                StudentName = StringValue(user, "name"),
                StudentIdNumber = StringValue(user, "student_id_number"),
                YearLevel = StringValue(profile, "year_level"),
                FromProgram = StringValue(request, "from_program"),
                ToProgram = StringValue(request, "to_program"),
                AverageGrade = profile?["average_grade"]?.GetValue<decimal?>(),
                Reason = StringValue(request, "reason"),
                Status = StringValue(request, "status", "pending")
            };
        }).Where(item => item != null).Cast<ShiftingRequestItem>().OrderBy(item => item.Status).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAccreditSubjectAsync(int student_id, string prev_school, string prev_subject_code, string prev_subject_title, decimal prev_units, string prev_grade, int credited_to_subject_id)
    {
        _db.EnsureRegistrarSession();
        var school = (prev_school ?? "").Trim();
        var previousCode = (prev_subject_code ?? "").Trim().ToUpperInvariant();
        var grade = (prev_grade ?? "1.50").Trim();
        if (student_id <= 0 || string.IsNullOrWhiteSpace(school) || string.IsNullOrWhiteSpace(previousCode) || credited_to_subject_id <= 0)
            return SetMessage("Please fill in all crediting details.", "error", "crediting");

        var message = "Transferee subject accredited successfully.";
        try
        {
            _store.UpdateRoot(root =>
            {
                var subjects = CanonicalJsonStore.GetArray(root, "subjects");
                if (!subjects.OfType<JsonObject>().Any(item => IntValue(item, "id") == credited_to_subject_id))
                    throw new InvalidOperationException("The selected institutional subject was not found.");
                var credits = CanonicalJsonStore.GetOrCreateArray(root, "transferee_credited_subjects");
                if (credits.OfType<JsonObject>().Any(item => IntValue(item, "student_id") == student_id && IntValue(item, "credited_to_subject_id") == credited_to_subject_id && StringValue(item, "status") == "approved"))
                    throw new InvalidOperationException("This subject is already credited to the student.");
                credits.Add(new JsonObject
                {
                    ["id"] = NextId(credits),
                    ["student_id"] = student_id,
                    ["prev_school"] = school,
                    ["prev_subject_code"] = previousCode,
                    ["prev_subject_title"] = (prev_subject_title ?? "").Trim(),
                    ["prev_units"] = prev_units,
                    ["prev_grade"] = grade,
                    ["credited_to_subject_id"] = credited_to_subject_id,
                    ["status"] = "approved",
                    ["evaluated_by"] = _db.CurrentUserName,
                    ["evaluated_at"] = DateTime.UtcNow.ToString("O")
                });
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "user_id") == student_id);
                if (profile != null)
                {
                    var creditedUnits = (int)prev_units;
                    profile["completed_units"] = IntValue(profile, "completed_units") + creditedUnits;
                    profile["units_remaining"] = Math.Max(0, IntValue(profile, "units_remaining", 144) - creditedUnits);
                }
            });
            await _db.LogActivityAsync(student_id, $"Transferee subject {previousCode} accredited to institutional curriculum by {_db.CurrentUserName}.");
        }
        catch (Exception ex) { message = ex.Message; return SetMessage(message, "error", "crediting"); }
        return SetMessage(message, "success", "crediting");
    }

    public async Task<IActionResult> OnPostShiftingActionAsync(int request_id, string action)
    {
        _db.EnsureRegistrarSession();
        var message = "Shifting request is no longer pending.";
        var success = false;
        var studentId = 0;
        var targetProgram = "";
        _store.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "course_shifting_requests");
            var request = requests.OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == request_id && StringValue(item, "status") == "pending");
            if (request == null) return;
            studentId = IntValue(request, "student_id");
            targetProgram = StringValue(request, "to_program");
            var approved = action != "reject_shifting";
            if (approved)
            {
                var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "user_id") == studentId);
                if (profile == null) throw new InvalidOperationException("Student profile was not found.");
                profile["program"] = targetProgram;
                request["status"] = "approved";
                message = $"Course shifting approved. Student program updated to {targetProgram}.";
            }
            else
            {
                request["status"] = "rejected";
                message = "Course shifting petition rejected.";
            }
            request["approved_at"] = DateTime.UtcNow.ToString("O");
            request["evaluated_by"] = _db.CurrentUserName;
            success = true;
        });

        if (success) await _db.LogActivityAsync(studentId, message);
        return SetMessage(message, success ? "success" : "error", "shifting");
    }

    private IActionResult SetMessage(string message, string type, string tab)
    {
        TempData["FlashMessage"] = message;
        TempData["FlashType"] = type;
        return RedirectToPage("/CurriculumEvaluation", new { tab });
    }

    private static int NextId(JsonArray rows) => rows.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
    private static int IntValue(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static decimal DecimalValue(JsonObject? item, string property) => item?[property]?.GetValue<decimal>() ?? 0m;
    private static string StringValue(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;

    private static string ReadPrerequisite(JsonObject subject)
    {
        if (subject["prerequisites"] is JsonArray array) return array.FirstOrDefault()?.GetValue<string>() ?? "";
        return subject["prerequisites"]?.GetValue<string>() ?? "";
    }
}
