using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace FacultyPortalMain.Services;

/// <summary>Raised when attendance, an assignment or a grade cannot be saved. The message is safe to show.</summary>
public sealed class CourseworkRejectedException(string message) : Exception(message);

/// <summary>
/// Attendance, assignments, submissions and feedback for the sections an instructor teaches.
/// Owns <c>attendance_logs</c>, <c>assignments</c> and <c>assignment_submissions</c> (Student Portal adds submissions).
/// Every method takes the instructor's <c>users.id</c>; null means an administrator, who may use any section.
/// </summary>
public sealed class CourseworkStore(CanonicalJsonStore store)
{
    public static readonly string[] Types = ["Activity", "Quiz", "Lab", "Project", "Exam"];
    public static readonly string[] AttendanceStatuses = ["Present", "Late", "Absent", "Excused"];
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Closed = "Closed";
    private const int MaxTextLength = 10_000;

    public sealed record AttendanceRow(int StudentId, string Name, string Number, string? Status);
    public sealed record AttendanceSheet(string Topic, IReadOnlyList<AttendanceRow> Rows);
    public sealed record AttendanceSummaryRow(int StudentId, string Name, string Number, int Present, int Late, int Absent, int Excused)
    {
        public int Sessions => Present + Late + Absent + Excused;
        /// <summary>Present and late count as attended.</summary>
        public double Rate => Sessions == 0 ? 0 : Math.Round((Present + Late) * 100.0 / Sessions, 1);
    }

    public sealed record AssignmentRow(int Id, int OfferingId, string SectionCode, string SubjectCode, string Title, string Type,
        DateTime? DueDate, decimal MaxScore, string Status, int Submitted, int Graded, int Enrolled);
    public sealed record SubmissionRow(int Id, int StudentId, string Name, string Number, string Text, DateTime? SubmittedAt, bool IsLate,
        decimal? Score, string Feedback, DateTime? GradedAt);
    public sealed record AssignmentDetail(AssignmentRow Header, string Instructions, IReadOnlyList<SubmissionRow> Submissions);
    public sealed record AssignmentInput(int? Id, int OfferingId, string Title, string Instructions, string Type, DateTime? DueDate, decimal MaxScore, bool Publish);

    // ── Attendance ──────────────────────────────────────────────────────────

    public AttendanceSheet GetAttendanceSheet(int offeringId, DateOnly date, int? instructorId)
    {
        var root = store.ReadRoot();
        RequireOffering(root, offeringId, instructorId);
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var logs = Rows(root, "attendance_logs").Where(l => Int(l, "class_offering_id") == offeringId && Str(l, "session_date") == day).ToList();

        var rows = EnrolledStudents(root, offeringId)
            .Select(s => new AttendanceRow(s.Id, s.Name, s.Number, logs.FirstOrDefault(l => Int(l, "student_id") == s.Id) is { } log ? Str(log, "status") : null))
            .ToList();
        return new AttendanceSheet(logs.Select(l => Str(l, "topic")).FirstOrDefault(t => t.Length > 0) ?? "", rows);
    }

    /// <summary>Records one session. Saving the same date again replaces the earlier marks. Returns how many students were marked.</summary>
    public int SaveAttendance(int offeringId, DateOnly date, string? topic, IReadOnlyDictionary<int, string> statuses, int? instructorId)
    {
        if (date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw new CourseworkRejectedException("Attendance cannot be recorded for a future date.");
        if (statuses.Count == 0) throw new CourseworkRejectedException("There are no students to mark.");
        foreach (var status in statuses.Values)
            if (!AttendanceStatuses.Contains(status, StringComparer.Ordinal))
                throw new CourseworkRejectedException($"'{status}' is not an attendance status.");

        var saved = 0;
        store.UpdateRoot(root =>
        {
            var offering = RequireOffering(root, offeringId, instructorId);
            var enrolled = EnrolledStudents(root, offeringId).Select(s => s.Id).ToHashSet();
            var unknown = statuses.Keys.FirstOrDefault(id => !enrolled.Contains(id));
            if (unknown != 0) throw new CourseworkRejectedException($"Student {unknown} is not enrolled in this section.");

            var logs = CanonicalJsonStore.GetOrCreateArray(root, "attendance_logs");
            var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var now = Now();
            var by = instructorId ?? Int(offering, "instructor_id");

            foreach (var (studentId, status) in statuses)
            {
                var row = logs.OfType<JsonObject>().FirstOrDefault(l =>
                    Int(l, "class_offering_id") == offeringId && Int(l, "student_id") == studentId && Str(l, "session_date") == day);
                if (row is null)
                {
                    row = new JsonObject
                    {
                        ["id"] = CanonicalJsonStore.NextId(logs),
                        ["class_offering_id"] = offeringId,
                        ["student_id"] = studentId,
                        ["session_date"] = day
                    };
                    logs.Add(row);
                }
                row["status"] = status;
                row["topic"] = (topic ?? "").Trim();
                row["recorded_by"] = by;
                row["recorded_at"] = now;
                saved++;
            }
        });
        return saved;
    }

    public IReadOnlyList<AttendanceSummaryRow> GetAttendanceSummary(int offeringId, int? instructorId)
    {
        var root = store.ReadRoot();
        RequireOffering(root, offeringId, instructorId);
        var logs = Rows(root, "attendance_logs").Where(l => Int(l, "class_offering_id") == offeringId).ToList();
        return EnrolledStudents(root, offeringId).Select(s =>
        {
            int Count(string status) => logs.Count(l => Int(l, "student_id") == s.Id && Str(l, "status") == status);
            return new AttendanceSummaryRow(s.Id, s.Name, s.Number, Count("Present"), Count("Late"), Count("Absent"), Count("Excused"));
        }).ToList();
    }

    // ── Assignments ─────────────────────────────────────────────────────────

    public IReadOnlyList<AssignmentRow> ListAssignments(int? instructorId, int? offeringId = null, string? status = null, string? search = null)
    {
        var root = store.ReadRoot();
        var rows = Rows(root, "assignments")
            .Where(a => OwnsOffering(root, Int(a, "class_offering_id"), instructorId))
            .Where(a => offeringId is null || Int(a, "class_offering_id") == offeringId)
            .Where(a => string.IsNullOrWhiteSpace(status) || Same(Str(a, "status"), status))
            .Where(a => string.IsNullOrWhiteSpace(search) || Str(a, "title").Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(a => ToRow(root, a))
            .OrderByDescending(a => a.Id)
            .ToList();
        return rows;
    }

    public AssignmentDetail? GetAssignment(int id, int? instructorId)
    {
        var root = store.ReadRoot();
        var assignment = Rows(root, "assignments").FirstOrDefault(a => Int(a, "id") == id);
        if (assignment is null || !OwnsOffering(root, Int(assignment, "class_offering_id"), instructorId)) return null;

        var users = Rows(root, "users").ToList();
        var submissions = Rows(root, "assignment_submissions").Where(s => Int(s, "assignment_id") == id)
            .Select(s =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(s, "student_id"));
                return new SubmissionRow(Int(s, "id"), Int(s, "student_id"), Str(user, "name"), Str(user, "student_id_number"),
                    Str(s, "text"), Date(s, "submitted_at"), Bool(s, "is_late"),
                    s["score"] is JsonValue ? Dec(s, "score") : null, Str(s, "feedback"), Date(s, "graded_at"));
            })
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AssignmentDetail(ToRow(root, assignment), Str(assignment, "instructions"), submissions);
    }

    /// <summary>Creates or edits an assignment. A published assignment needs a due date. A closed one cannot be edited.</summary>
    public int SaveAssignment(AssignmentInput input, int? instructorId)
    {
        var title = (input.Title ?? "").Trim();
        if (title.Length == 0 || title.Length > 200) throw new CourseworkRejectedException("Give the assignment a title of up to 200 characters.");
        if ((input.Instructions ?? "").Length > MaxTextLength) throw new CourseworkRejectedException("The instructions are too long.");
        if (!Types.Contains(input.Type, StringComparer.Ordinal)) throw new CourseworkRejectedException("Choose an assignment type.");
        if (input.MaxScore is <= 0 or > 1000) throw new CourseworkRejectedException("The maximum score must be between 1 and 1000.");
        if (input.Publish && input.DueDate is null) throw new CourseworkRejectedException("Set a due date before publishing.");

        var id = 0;
        store.UpdateRoot(root =>
        {
            var offering = RequireOffering(root, input.OfferingId, instructorId);
            var rows = CanonicalJsonStore.GetOrCreateArray(root, "assignments");
            JsonObject row;
            if (input.Id is { } existingId)
            {
                row = rows.OfType<JsonObject>().FirstOrDefault(a => Int(a, "id") == existingId)
                      ?? throw new CourseworkRejectedException("That assignment no longer exists.");
                RequireOffering(root, Int(row, "class_offering_id"), instructorId);
                if (Same(Str(row, "status"), Closed)) throw new CourseworkRejectedException("A closed assignment cannot be edited. Reopen it first.");
                if (Int(row, "class_offering_id") != input.OfferingId && SubmissionCount(root, existingId) > 0)
                    throw new CourseworkRejectedException("An assignment with submissions cannot move to another section.");
            }
            else
            {
                row = new JsonObject { ["id"] = CanonicalJsonStore.NextId(rows), ["status"] = Draft, ["created_at"] = Now(), ["created_by"] = instructorId ?? Int(offering, "instructor_id") };
                rows.Add(row);
            }

            row["class_offering_id"] = input.OfferingId;
            row["title"] = title;
            row["instructions"] = (input.Instructions ?? "").Trim();
            row["type"] = input.Type;
            row["max_score"] = input.MaxScore;
            row["due_date"] = input.DueDate is { } due ? due.ToString("O", CultureInfo.InvariantCulture) : null;
            if (input.Publish && !Same(Str(row, "status"), Published))
            {
                row["status"] = Published;
                row["published_at"] = Now();
            }
            id = Int(row, "id");
        });
        return id;
    }

    /// <summary>Publishes a draft, closes a published assignment, or reopens a closed one.</summary>
    public void SetStatus(int id, string status, int? instructorId)
    {
        if (!new[] { Published, Closed }.Contains(status, StringComparer.Ordinal)) throw new CourseworkRejectedException("Unknown status.");
        store.UpdateRoot(root =>
        {
            var row = Rows(root, "assignments").FirstOrDefault(a => Int(a, "id") == id)
                      ?? throw new CourseworkRejectedException("That assignment no longer exists.");
            RequireOffering(root, Int(row, "class_offering_id"), instructorId);
            if (status == Published && row["due_date"] is null) throw new CourseworkRejectedException("Set a due date before publishing.");
            if (status == Closed && !Same(Str(row, "status"), Published)) throw new CourseworkRejectedException("Only a published assignment can be closed.");

            row["status"] = status;
            if (status == Published && Str(row, "published_at").Length == 0) row["published_at"] = Now();
        });
    }

    /// <summary>Deletes a draft that nobody has submitted to.</summary>
    public void DeleteAssignment(int id, int? instructorId)
    {
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, "assignments");
            var row = rows.OfType<JsonObject>().FirstOrDefault(a => Int(a, "id") == id)
                      ?? throw new CourseworkRejectedException("That assignment no longer exists.");
            RequireOffering(root, Int(row, "class_offering_id"), instructorId);
            if (!Same(Str(row, "status"), Draft)) throw new CourseworkRejectedException("Only a draft can be deleted. Close a published assignment instead.");
            if (SubmissionCount(root, id) > 0) throw new CourseworkRejectedException("This assignment already has submissions.");
            rows.Remove(row);
        });
    }

    /// <summary>Scores a submission, writes feedback, or both. A blank score keeps feedback-only.</summary>
    public void GradeSubmission(int submissionId, decimal? score, string? feedback, int? instructorId)
    {
        if ((feedback ?? "").Length > MaxTextLength) throw new CourseworkRejectedException("The feedback is too long.");
        store.UpdateRoot(root =>
        {
            var submission = Rows(root, "assignment_submissions").FirstOrDefault(s => Int(s, "id") == submissionId)
                             ?? throw new CourseworkRejectedException("That submission no longer exists.");
            var assignment = Rows(root, "assignments").FirstOrDefault(a => Int(a, "id") == Int(submission, "assignment_id"))
                             ?? throw new CourseworkRejectedException("The assignment for that submission no longer exists.");
            RequireOffering(root, Int(assignment, "class_offering_id"), instructorId);

            if (score is { } value)
            {
                if (value < 0 || value > Dec(assignment, "max_score"))
                    throw new CourseworkRejectedException($"The score must be between 0 and {Dec(assignment, "max_score"):0.##}.");
                submission["score"] = value;
                submission["graded_at"] = Now();
                submission["graded_by"] = instructorId ?? 0;
                submission["status"] = "Graded";
            }
            submission["feedback"] = (feedback ?? "").Trim();
        });
    }

    /// <summary>Submissions with a score or feedback recorded, newest first, for the Feedback page.</summary>
    public IReadOnlyList<(SubmissionRow Submission, string AssignmentTitle, string SectionCode, int AssignmentId)> RecentSubmissions(int? instructorId, int take = 30)
    {
        var root = store.ReadRoot();
        var users = Rows(root, "users").ToList();
        var assignments = Rows(root, "assignments").Where(a => OwnsOffering(root, Int(a, "class_offering_id"), instructorId)).ToList();
        var offerings = Rows(root, "class_offerings").ToList();
        return Rows(root, "assignment_submissions")
            .Select(s => (Submission: s, Assignment: assignments.FirstOrDefault(a => Int(a, "id") == Int(s, "assignment_id"))))
            .Where(x => x.Assignment is not null)
            .OrderByDescending(x => Str(x.Submission, "submitted_at"), StringComparer.Ordinal)
            .Take(take)
            .Select(x =>
            {
                var s = x.Submission;
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(s, "student_id"));
                var row = new SubmissionRow(Int(s, "id"), Int(s, "student_id"), Str(user, "name"), Str(user, "student_id_number"),
                    Str(s, "text"), Date(s, "submitted_at"), Bool(s, "is_late"), s["score"] is JsonValue ? Dec(s, "score") : null,
                    Str(s, "feedback"), Date(s, "graded_at"));
                var offering = offerings.FirstOrDefault(o => Int(o, "id") == Int(x.Assignment, "class_offering_id"));
                return (row, Str(x.Assignment, "title"), Str(offering, "section_code"), Int(x.Assignment, "id"));
            })
            .ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static JsonObject RequireOffering(JsonObject root, int offeringId, int? instructorId)
    {
        var offering = Rows(root, "class_offerings").FirstOrDefault(o => Int(o, "id") == offeringId)
                       ?? throw new CourseworkRejectedException("That section does not exist.");
        if (instructorId is not null && Int(offering, "instructor_id") != instructorId)
            throw new CourseworkRejectedException("That is not one of your sections.");
        return offering;
    }

    private static bool OwnsOffering(JsonObject root, int offeringId, int? instructorId) =>
        instructorId is null || Rows(root, "class_offerings").Any(o => Int(o, "id") == offeringId && Int(o, "instructor_id") == instructorId);

    private sealed record Student(int Id, string Name, string Number);

    private static List<Student> EnrolledStudents(JsonObject root, int offeringId)
    {
        var users = Rows(root, "users").ToList();
        return Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "class_offering_id") == offeringId && !Same(Str(e, "status"), "dropped"))
            .Select(e => Int(e, "student_id")).Distinct()
            .Select(id => users.FirstOrDefault(u => Int(u, "id") == id) is { } u ? new Student(id, Str(u, "name"), Str(u, "student_id_number")) : new Student(id, "Unknown student", ""))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int SubmissionCount(JsonObject root, int assignmentId) =>
        Rows(root, "assignment_submissions").Count(s => Int(s, "assignment_id") == assignmentId);

    private static AssignmentRow ToRow(JsonObject root, JsonObject a)
    {
        var offeringId = Int(a, "class_offering_id");
        var offering = Rows(root, "class_offerings").FirstOrDefault(o => Int(o, "id") == offeringId);
        var subject = Rows(root, "subjects").FirstOrDefault(s => Int(s, "id") == Int(offering, "subject_id"));
        var submissions = Rows(root, "assignment_submissions").Where(s => Int(s, "assignment_id") == Int(a, "id")).ToList();
        return new AssignmentRow(Int(a, "id"), offeringId, Str(offering, "section_code"), Str(subject, "subject_code"), Str(a, "title"),
            Str(a, "type"), Date(a, "due_date"), Dec(a, "max_score"), Str(a, "status"),
            submissions.Count, submissions.Count(s => s["score"] is JsonValue), EnrolledStudents(root, offeringId).Count);
    }

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? Date(JsonObject? o, string key) =>
        DateTimeOffset.TryParse(Str(o, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;

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
