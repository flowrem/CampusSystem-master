using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class GradeControlModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CanonicalJsonStore _store;

    public GradeControlModel(DatabaseService db, SettingsService settings, CanonicalJsonStore store)
    {
        _db = db;
        _settings = settings;
        _store = store;
    }

    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string CurrentSemester { get; set; } = "1st Semester";
    public bool EncodingOpen { get; set; } = true;
    public string EncodingDeadline { get; set; } = "2025-10-30";
    public string ActiveTab { get; set; } = "encoding";
    public List<OfferingSimpleItem> OfferingsList { get; set; } = new();
    public int SelectedOfferingId { get; set; }
    public OfferingDetailItem? SelectedOffering { get; set; }
    public List<GradeSheetRow> GradeSheet { get; set; } = new();
    public List<PendingRevisionItem> PendingRevisions { get; set; } = new();

    public class OfferingSimpleItem
    {
        public int Id { get; set; }
        public string SectionCode { get; set; } = "";
        public string Room { get; set; } = "";
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public string InstructorName { get; set; } = "";
    }

    public class OfferingDetailItem
    {
        public int Id { get; set; }
        public string SectionCode { get; set; } = "";
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
        public string InstructorName { get; set; } = "";
    }

    public class GradeSheetRow
    {
        public int EnrolledSubId { get; set; }
        public int StudentId { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public int? GradeId { get; set; }
        public decimal? Grade { get; set; }
        public bool IsInc { get; set; }
        public string? GradeStatus { get; set; }
        public string? Remarks { get; set; }
    }

    public class PendingRevisionItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public int GradeId { get; set; }
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal? CurrentGrade { get; set; }
        public bool CurrentIsInc { get; set; }
        public decimal? RequestedGrade { get; set; }
        public string? Reason { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string tab = "encoding", int offering_id = 0)
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "encoding", "verification", "revisions" }.Contains(tab) ? tab : "encoding";
        CurrentSchoolYear = await _settings.GetSchoolYearAsync();
        CurrentSemester = await _settings.GetSemesterAsync();
        EncodingOpen = await _settings.IsEncodingOpenAsync();
        EncodingDeadline = await _settings.GetEncodingDeadlineAsync();

        var root = _store.ReadRoot();
        var offerings = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().ToList();
        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();

        OfferingsList = offerings.Select(offering =>
        {
            var subject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(offering, "subject_id"));
            return new OfferingSimpleItem
            {
                Id = IntValue(offering, "id"),
                SectionCode = StringValue(offering, "section_code"),
                Room = StringValue(offering, "room"),
                SubjectCode = StringValue(subject, "subject_code"),
                SubjectName = StringValue(subject, "subject_name"),
                InstructorName = StringValue(offering, "instructor_name")
            };
        }).OrderBy(item => item.SectionCode).ThenBy(item => item.SubjectCode).ToList();

        SelectedOfferingId = offering_id > 0 ? offering_id : OfferingsList.FirstOrDefault()?.Id ?? 0;
        var selected = offerings.FirstOrDefault(item => IntValue(item, "id") == SelectedOfferingId);
        if (selected != null)
        {
            var subject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(selected, "subject_id"));
            SelectedOffering = new OfferingDetailItem
            {
                Id = SelectedOfferingId,
                SectionCode = StringValue(selected, "section_code"),
                SubjectCode = StringValue(subject, "subject_code"),
                SubjectName = StringValue(subject, "subject_name"),
                Units = DecimalValue(subject, "units"),
                InstructorName = StringValue(selected, "instructor_name")
            };

            GradeSheet = enrolledSubjects.Where(item => IntValue(item, "class_offering_id") == SelectedOfferingId && StringValue(item, "status") == "enrolled")
                .Select(item =>
                {
                    var enrollment = enrollments.FirstOrDefault(entry => IntValue(entry, "id") == IntValue(item, "enrollment_id"));
                    var studentId = IntValue(item, "student_id", IntValue(enrollment, "student_id"));
                    var user = users.FirstOrDefault(entry => IntValue(entry, "id") == studentId);
                    var profile = profiles.FirstOrDefault(entry => IntValue(entry, "user_id") == studentId);
                    var grade = grades.FirstOrDefault(entry => IntValue(entry, "enrolled_subject_id") == IntValue(item, "id"));
                    if (user == null) return null;
                    return new GradeSheetRow
                    {
                        EnrolledSubId = IntValue(item, "id"),
                        StudentId = studentId,
                        Name = StringValue(user, "name"),
                        StudentIdNumber = StringValue(user, "student_id_number"),
                        Program = StringValue(profile, "program"),
                        YearLevel = StringValue(profile, "year_level"),
                        GradeId = grade == null ? null : IntValue(grade, "id"),
                        Grade = grade?["final_grade"]?.GetValue<decimal?>(),
                        IsInc = grade?["is_inc"]?.GetValue<bool>() == true,
                        GradeStatus = grade == null ? null : StringValue(grade, "status", "verified"),
                        Remarks = StringValue(grade, "remarks")
                    };
                }).Where(item => item != null).Cast<GradeSheetRow>().OrderBy(item => item.Name).ToList();
        }

        PendingRevisions = CanonicalJsonStore.GetArray(root, "completion_revision_requests").OfType<JsonObject>()
            .Where(item => StringValue(item, "status") == "pending")
            .Select(request =>
            {
                var studentId = IntValue(request, "student_id");
                var user = users.FirstOrDefault(item => IntValue(item, "id") == studentId);
                var grade = grades.FirstOrDefault(item => IntValue(item, "id") == IntValue(request, "grade_id"));
                var subjectCode = StringValue(request, "subject_code");
                var subject = subjects.FirstOrDefault(item => StringValue(item, "subject_code") == subjectCode);
                if (user == null) return null;
                return new PendingRevisionItem
                {
                    Id = IntValue(request, "id"),
                    StudentId = studentId,
                    StudentName = StringValue(user, "name"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    GradeId = IntValue(request, "grade_id"),
                    SubjectCode = subjectCode,
                    SubjectName = StringValue(subject, "subject_name", subjectCode),
                    CurrentGrade = grade?["final_grade"]?.GetValue<decimal?>(),
                    CurrentIsInc = grade?["is_inc"]?.GetValue<bool>() == true,
                    RequestedGrade = request["requested_grade"]?.GetValue<decimal?>(),
                    Reason = StringValue(request, "reason"),
                    RequestedAt = DateValue(request, "requested_at", DateTime.UtcNow)
                };
            }).Where(item => item != null).Cast<PendingRevisionItem>().OrderBy(item => item.RequestedAt).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostSaveWindowAsync(string? window_open, string? deadline)
    {
        _db.EnsureRegistrarSession();
        var open = window_open == "1" ? "1" : "0";
        await _db.SetSettingAsync("grade_encoding_open", open);
        await _db.SetSettingAsync("grade_encoding_deadline", (deadline ?? "").Trim());
        await _db.LogActivityAsync(_db.CurrentUserId, $"Grade encoding window set to {(open == "1" ? "OPEN" : "CLOSED")} by {_db.CurrentUserName}.");
        TempData["FlashMessage"] = "Grade encoding window settings updated.";
        TempData["FlashType"] = "success";
        return RedirectToPage("/GradeControl", new { tab = "encoding" });
    }

    public async Task<IActionResult> OnPostBatchSaveGradesAsync(int offering_id, IFormCollection form)
    {
        _db.EnsureRegistrarSession();
        EncodingOpen = await _settings.IsEncodingOpenAsync();
        if (!EncodingOpen)
        {
            TempData["FlashMessage"] = "Grade encoding window is currently CLOSED.";
            TempData["FlashType"] = "error";
            return RedirectToPage("/GradeControl", new { tab = "encoding", offering_id });
        }

        var error = "";
        var savedCount = 0;
        _store.UpdateRoot(root =>
        {
            var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
            var grades = CanonicalJsonStore.GetOrCreateArray(root, "grades");
            foreach (var key in form.Keys.Where(key => key.StartsWith("grades[", StringComparison.Ordinal)))
            {
                var idString = key.Substring(7, key.Length - 8);
                if (!int.TryParse(idString, out var enrolledSubjectId)) continue;
                var enrolled = enrolledSubjects.FirstOrDefault(item => IntValue(item, "id") == enrolledSubjectId && IntValue(item, "class_offering_id") == offering_id);
                if (enrolled == null) continue;
                var isInc = form.ContainsKey($"is_inc[{enrolledSubjectId}]") && form[$"is_inc[{enrolledSubjectId}]"] == "1";
                var gradeText = form[key].ToString().Trim();
                if (!isInc && (string.IsNullOrEmpty(gradeText) || !decimal.TryParse(gradeText, out var parsedGrade) || parsedGrade < 1m || parsedGrade > 5m)) continue;

                decimal? gradeValue = isInc ? null : decimal.Parse(gradeText);
                var remarks = isInc ? "Incomplete (INC)" : gradeValue <= 3m ? "Passed" : "Failed";
                var existing = grades.OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "enrolled_subject_id") == enrolledSubjectId);
                if (existing != null && StringValue(existing, "status") == "locked") continue;
                if (existing == null)
                {
                    existing = new JsonObject { ["id"] = NextId(grades), ["enrolled_subject_id"] = enrolledSubjectId };
                    grades.Add(existing);
                }
                existing["student_id"] = IntValue(enrolled, "student_id");
                existing["subject_code"] = StringValue(enrolled, "subject_code");
                existing["final_grade"] = gradeValue;
                existing["is_inc"] = isInc;
                existing["status"] = "draft";
                existing["remarks"] = remarks;
                existing["encoded_by"] = _db.CurrentUserId;
                existing["encoded_at"] = DateTime.UtcNow.ToString("O");
                savedCount++;
            }
        });

        if (!string.IsNullOrEmpty(error))
        {
            TempData["FlashMessage"] = error;
            TempData["FlashType"] = "error";
        }
        else
        {
            await _db.LogActivityAsync(_db.CurrentUserId, $"Grades encoded for offering #{offering_id} by {_db.CurrentUserName}.");
            TempData["FlashMessage"] = $"{savedCount} grade entr{(savedCount == 1 ? "y" : "ies")} saved as Draft.";
            TempData["FlashType"] = "success";
        }
        return RedirectToPage("/GradeControl", new { tab = "encoding", offering_id });
    }

    public async Task<IActionResult> OnPostVerifyOrLockAsync(int offering_id, string action)
    {
        _db.EnsureRegistrarSession();
        var lockGrades = action == "lock_section";
        var verifiedStudents = new HashSet<int>();
        _store.UpdateRoot(root =>
        {
            var enrolled = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().Where(item => IntValue(item, "class_offering_id") == offering_id).ToList();
            var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();
            foreach (var subject in enrolled)
            {
                var grade = grades.FirstOrDefault(item => IntValue(item, "enrolled_subject_id") == IntValue(subject, "id"));
                if (grade == null) continue;
                var status = StringValue(grade, "status", "verified");
                if (!lockGrades && status == "draft") grade["status"] = "verified";
                else if (lockGrades && status is "draft" or "verified")
                {
                    grade["status"] = "locked";
                    grade["locked_at"] = DateTime.UtcNow.ToString("O");
                }
                else continue;
                if (lockGrades) grade["verified_by"] = _db.CurrentUserId;
                verifiedStudents.Add(IntValue(subject, "student_id"));
            }
            foreach (var studentId in verifiedStudents) RecalculateStudentGwa(root, studentId);
        });

        var statusMessage = lockGrades ? "locked" : "verified";
        await _db.LogActivityAsync(_db.CurrentUserId, $"Grades {statusMessage} for class offering #{offering_id} by {_db.CurrentUserName}.");
        TempData["FlashMessage"] = $"Class offering grades {statusMessage} and student GWAs recalculated.";
        TempData["FlashType"] = "success";
        return RedirectToPage("/GradeControl", new { tab = "verification", offering_id });
    }

    public async Task<IActionResult> OnPostRevisionActionAsync(int request_id, string action)
    {
        _db.EnsureRegistrarSession();
        var approved = action == "approve_revision";
        var message = "Petition is no longer pending.";
        var updatedStudentId = 0;
        _store.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetArray(root, "completion_revision_requests");
            var request = requests.OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == request_id && StringValue(item, "status") == "pending");
            if (request == null) return;
            updatedStudentId = IntValue(request, "student_id");
            if (approved)
            {
                var grade = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == IntValue(request, "grade_id"));
                if (grade == null) throw new InvalidOperationException("The grade record for this petition was not found.");
                var requestedGrade = request["requested_grade"]?.GetValue<decimal?>();
                grade["final_grade"] = requestedGrade;
                grade["is_inc"] = false;
                grade["status"] = "verified";
                grade["remarks"] = requestedGrade <= 3m ? "Passed (INC Completed)" : "Failed";
                grade["verified_by"] = _db.CurrentUserId;
                grade["verified_at"] = DateTime.UtcNow.ToString("O");
                RecalculateStudentGwa(root, updatedStudentId);
                message = "Grade completion approved and applied to permanent record.";
            }
            else message = "Grade revision petition rejected.";
            request["status"] = approved ? "approved" : "rejected";
            request["processed_at"] = DateTime.UtcNow.ToString("O");
            request["processed_by"] = _db.CurrentUserId;
        });

        TempData["FlashMessage"] = message;
        TempData["FlashType"] = updatedStudentId > 0 ? "success" : "error";
        if (updatedStudentId > 0) await _db.LogActivityAsync(updatedStudentId, message);
        return RedirectToPage("/GradeControl", new { tab = "revisions" });
    }

    private static void RecalculateStudentGwa(JsonObject root, int studentId)
    {
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();
        decimal totalPoints = 0m;
        decimal totalUnits = 0m;
        foreach (var grade in grades)
        {
            if (grade["final_grade"] == null || grade["is_inc"]?.GetValue<bool>() == true || !new[] { "verified", "locked" }.Contains(StringValue(grade, "status"), StringComparer.OrdinalIgnoreCase)) continue;
            var subject = enrolledSubjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(grade, "enrolled_subject_id"));
            if (subject == null) continue;
            var enrollment = enrollments.FirstOrDefault(item => IntValue(item, "id") == IntValue(subject, "enrollment_id"));
            if (IntValue(enrollment, "student_id") != studentId) continue;
            var units = DecimalValue(subject, "units");
            totalPoints += grade["final_grade"]!.GetValue<decimal>() * units;
            totalUnits += units;
        }
        if (totalUnits <= 0) return;
        var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "user_id") == studentId);
        if (profile != null) profile["average_grade"] = Math.Round(totalPoints / totalUnits, 2);
    }

    private static int NextId(JsonArray rows) => rows.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
    private static string StringValue(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int IntValue(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static decimal DecimalValue(JsonObject? item, string property) => item?[property]?.GetValue<decimal>() ?? 0m;
    private static DateTime DateValue(JsonObject? item, string property, DateTime fallback) => DateTime.TryParse(StringValue(item, property), out var value) ? value : fallback;
}
