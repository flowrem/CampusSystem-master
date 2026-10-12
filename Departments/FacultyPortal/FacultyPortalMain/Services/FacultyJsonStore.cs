using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace FacultyPortalMain.Services;

/// <summary>Raised when a grade cannot be saved. The message is safe to show to the instructor.</summary>
public sealed class GradeRejectedException(string message) : Exception(message);

/// <summary>
/// Faculty Portal's view of the shared JSON datastore. Reads class offerings, rosters and Finance clearance;
/// owns writes to <c>grades</c>, which Registrar then verifies and locks in Grade Control.
/// </summary>
public sealed class FacultyJsonStore(CanonicalJsonStore store)
{
    public class ClassOfferingItem
    {
        public int Id { get; set; }
        public string SectionCode { get; set; } = "";
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
        public string Room { get; set; } = "";
        public string DaysOfWeek { get; set; } = "";
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public string InstructorName { get; set; } = "";
        public int EnrolledCount { get; set; }
    }

    public class StudentRosterItem
    {
        public int StudentId { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string FinanceClearanceStatus { get; set; } = "NotCleared";
        public decimal? FinalGrade { get; set; }
        public bool IsInc { get; set; }
        public string Remarks { get; set; } = "";
        /// <summary>"draft", "verified" or "locked". Empty when no grade has been encoded.</summary>
        public string GradeStatus { get; set; } = "";
        public int EnrolledSubjectId { get; set; }

        public bool GradeIsFrozen => GradeStatus is "verified" or "locked";
    }

    public bool IsEncodingOpen() =>
        Setting(store.ReadRoot(), "grade_encoding_open", "1") == "1";

    /// <summary>Offerings for the current term. With an instructor id (users.id), only that instructor's sections; null lists all.</summary>
    public List<ClassOfferingItem> GetClassOfferings(int? instructorId = null)
    {
        var root = store.ReadRoot();
        var year = Setting(root, "current_school_year", "2025-2026");
        var semester = Setting(root, "current_semester", "1st Semester");
        var subjects = Rows(root, "subjects").ToList();
        var enrolled = Rows(root, "enrolled_subjects").Where(e => !Same(Str(e, "status"), "dropped")).ToList();

        return Rows(root, "class_offerings")
            .Where(o => Same(Str(o, "school_year"), year) && Same(Str(o, "semester"), semester))
            .Where(o => instructorId is null || Int(o, "instructor_id") == instructorId)
            .OrderBy(o => Str(o, "section_code"), StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var subject = subjects.FirstOrDefault(s => Int(s, "id") == Int(o, "subject_id"));
                return new ClassOfferingItem
                {
                    Id = Int(o, "id"),
                    SectionCode = Str(o, "section_code"),
                    SubjectCode = Str(subject, "subject_code"),
                    SubjectName = Str(subject, "subject_name"),
                    Units = Dec(subject, "units"),
                    Room = Str(o, "room"),
                    DaysOfWeek = Str(o, "days_of_week"),
                    StartTime = Str(o, "start_time"),
                    EndTime = Str(o, "end_time"),
                    InstructorName = Str(o, "instructor_name"),
                    EnrolledCount = enrolled.Count(e => Int(e, "class_offering_id") == Int(o, "id"))
                };
            })
            .ToList();
    }

    /// <summary>Students enrolled in the offering with their grade and Finance clearance. An unknown offering gives an empty list.</summary>
    public List<StudentRosterItem> GetClassRoster(int classOfferingId, int? instructorId = null)
    {
        var root = store.ReadRoot();
        if (instructorId is not null
            && !Rows(root, "class_offerings").Any(o => Int(o, "id") == classOfferingId && Int(o, "instructor_id") == instructorId))
            return [];

        var users = Rows(root, "users").ToList();
        var profiles = Rows(root, "student_profiles").ToList();
        var grades = Rows(root, "grades").ToList();
        var clearances = Rows(root, "student_clearance")
            .Where(c => Str(c, "department_name").Contains("Finance", StringComparison.OrdinalIgnoreCase)).ToList();

        return Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "class_offering_id") == classOfferingId && !Same(Str(e, "status"), "dropped"))
            .Select(e =>
            {
                var studentId = Int(e, "student_id");
                var user = users.FirstOrDefault(u => Int(u, "id") == studentId);
                var profile = profiles.FirstOrDefault(p => Int(p, "user_id") == studentId);
                var grade = grades.FirstOrDefault(g => Int(g, "enrolled_subject_id") == Int(e, "id"));
                var clearance = clearances.Where(c => Int(c, "student_id") == studentId).OrderBy(c => Int(c, "id")).LastOrDefault();
                return new StudentRosterItem
                {
                    StudentId = studentId,
                    StudentIdNumber = Str(user, "student_id_number"),
                    Name = user is null ? "Unknown student" : Str(user, "name"),
                    Program = Str(profile, "program"),
                    YearLevel = Str(profile, "year_level"),
                    // No Finance row means no clearance, so no exam permit.
                    FinanceClearanceStatus = clearance is null ? "NotCleared"
                        : Same(Str(clearance, "status"), "Cleared") ? "Cleared"
                        : Same(Str(clearance, "status"), "Conditional") ? "Conditional" : "NotCleared",
                    FinalGrade = grade?["final_grade"] is JsonValue ? Dec(grade, "final_grade") : null,
                    IsInc = Bool(grade, "is_inc"),
                    Remarks = Str(grade, "remarks"),
                    // Grades from before the status field existed were already final.
                    GradeStatus = grade is null ? "" : Str(grade, "status") is { Length: > 0 } status ? status : "locked",
                    EnrolledSubjectId = Int(e, "id")
                };
            })
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Saves a final grade as a draft for Registrar to verify and lock. Refuses when the encoding window is
    /// closed, the grade is verified or locked, the enrollment is dropped, or the grade is outside 1.00–5.00.
    /// A failing grade (above 3.00) also opens a Guidance referral.
    /// </summary>
    public void SaveGrade(int enrolledSubjectId, decimal grade, string? facultyNote, int? instructorId = null)
    {
        if (grade < 1.00m || grade > 5.00m)
            throw new GradeRejectedException("A grade must be between 1.00 and 5.00.");

        Encode(enrolledSubjectId, facultyNote, instructorId, (row, _) =>
        {
            row["final_grade"] = grade;
            row["is_inc"] = false;
            row["inc_reason"] = null;
            row["remarks"] = grade <= 3.00m ? "Passed" : "Failed";
            return (grade > 3.00m ? $"failing grade {grade:0.00}" : null, $"Grade {grade:0.00}");
        });
    }

    /// <summary>Marks the subject Incomplete (INC) as a draft, which also opens a Guidance referral.</summary>
    public void SaveIncomplete(int enrolledSubjectId, string? reason, int? instructorId = null)
    {
        Encode(enrolledSubjectId, null, instructorId, (row, _) =>
        {
            row["final_grade"] = null;
            row["is_inc"] = true;
            row["inc_reason"] = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            row["remarks"] = "Incomplete (INC)";
            return ("an incomplete (INC) mark", "INC");
        });
    }

    /// <summary>Shared by grades and INC: guards, the grade row, the activity log and the Guidance referral, in one locked write.</summary>
    private void Encode(int enrolledSubjectId, string? facultyNote, int? instructorId, Func<JsonObject, JsonObject, (string? ReferralReason, string Summary)> fill)
    {
        store.UpdateRoot(root =>
        {
            if (Setting(root, "grade_encoding_open", "1") != "1")
                throw new GradeRejectedException("The grade encoding window is closed.");

            var enrolled = Rows(root, "enrolled_subjects").FirstOrDefault(e =>
                    Int(e, "id") == enrolledSubjectId && !Same(Str(e, "status"), "dropped"))
                ?? throw new GradeRejectedException("That student is not enrolled in the section.");
            var offering = Rows(root, "class_offerings").FirstOrDefault(o => Int(o, "id") == Int(enrolled, "class_offering_id"));
            if (instructorId is not null && (offering is null || Int(offering, "instructor_id") != instructorId))
                throw new GradeRejectedException("That student is not in one of your sections.");

            var grades = CanonicalJsonStore.GetOrCreateArray(root, "grades");
            var row = grades.OfType<JsonObject>().FirstOrDefault(g => Int(g, "enrolled_subject_id") == enrolledSubjectId);
            if (row is not null && Str(row, "status") is not ("draft"))
                throw new GradeRejectedException("This grade is already final. Ask the Registrar for a revision.");

            if (row is null)
            {
                row = new JsonObject { ["id"] = CanonicalJsonStore.NextId(grades), ["enrolled_subject_id"] = enrolledSubjectId };
                grades.Add(row);
            }

            var studentId = Int(enrolled, "student_id");
            row["student_id"] = studentId;
            row["subject_code"] = Str(enrolled, "subject_code");
            row["school_year"] = Str(offering, "school_year");
            row["semester"] = Str(offering, "semester");
            row["status"] = "draft";
            var (referralReason, summary) = fill(row, enrolled);
            if (!string.IsNullOrWhiteSpace(facultyNote)) row["faculty_note"] = facultyNote.Trim();
            row["encoded_by_faculty_id"] = Int(offering, "instructor_id");
            row["encoded_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var log = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
            log.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(log),
                ["user_id"] = Int(offering, "instructor_id"),
                ["user_name"] = Str(offering, "instructor_name"),
                ["message"] = $"{summary} encoded for {Str(enrolled, "subject_code")} (enrolled subject #{enrolledSubjectId}) via Faculty Portal.",
                ["created_at"] = now
            });

            if (referralReason is not null) OpenGuidanceReferral(root, enrolled, offering, referralReason, now);
        });
    }

    /// <summary>
    /// Files one ReferredIn request per enrolled subject. Re-grading never adds a second one, and a counselor's
    /// triage of the first is never overwritten.
    /// </summary>
    private static void OpenGuidanceReferral(JsonObject root, JsonObject enrolled, JsonObject? offering, string reason, string now)
    {
        var requests = CanonicalJsonStore.GetOrCreateArray(root, "guidance_requests");
        var key = $"grade-referral:{Int(enrolled, "id")}";
        if (requests.OfType<JsonObject>().Any(r => Str(r, "idempotency_key") == key)) return;

        requests.Add(new JsonObject
        {
            ["id"] = CanonicalJsonStore.NextId(requests),
            ["student_id"] = Int(enrolled, "student_id"),
            ["subject"] = $"Academic referral: {Str(enrolled, "subject_code")}",
            ["category"] = "Academic Counseling",
            ["details"] = $"Faculty ({(offering is null ? "unknown instructor" : Str(offering, "instructor_name"))}) recorded {reason} for {Str(enrolled, "subject_code")} {Str(enrolled, "subject_name")}.",
            ["safety_valve_text"] = null,
            ["urgency"] = "Normal",
            ["status"] = "ReferredIn",
            ["assigned_counselor_id"] = null,
            ["idempotency_key"] = key,
            ["enrolled_subject_id"] = Int(enrolled, "id"),
            ["row_version"] = "AQ==",
            ["created_at"] = now
        });
    }

    private static string Setting(JsonObject root, string key, string fallback) =>
        Rows(root, "settings").FirstOrDefault(s => Str(s, "key") == key) is { } row && Str(row, "value") is { Length: > 0 } value
            ? value
            : fallback;

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        CanonicalJsonStore.GetArray(root, collection).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Nodes read from disk hold JsonElements, but nodes built in this process hold the C# type
    // they were created with, so each reader accepts every numeric type.
    private static int Int(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<long>(out var l) ? (int)l
        : v.TryGetValue<decimal>(out var d) ? (int)d
        : 0;

    private static decimal Dec(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0m
        : v.TryGetValue<decimal>(out var d) ? d
        : v.TryGetValue<double>(out var f) ? (decimal)f
        : v.TryGetValue<long>(out var l) ? l
        : v.TryGetValue<int>(out var n) ? n
        : 0m;

    private static string Str(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static bool Bool(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
