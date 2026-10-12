using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using FacultyPortalMain.Services;

namespace FacultyPortalMain.Tests;

public sealed class CourseworkStoreTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "users": [
          { "id": 3, "student_id_number": "FAC-1", "name": "Prof. Turing", "role": "faculty" },
          { "id": 13, "student_id_number": "FAC-2", "name": "Prof. Other", "role": "faculty" },
          { "id": 5, "student_id_number": "2023-01045", "name": "Alyssa Mendoza", "role": "student" },
          { "id": 6, "student_id_number": "2023-01088", "name": "Juan Dela Cruz", "role": "student" },
          { "id": 7, "student_id_number": "2022-00512", "name": "Kathryn Bernardo", "role": "student" }
        ],
        "subjects": [ { "id": 101, "subject_code": "IT311", "subject_name": "Web Systems", "units": 3 } ],
        "class_offerings": [
          { "id": 501, "subject_id": 101, "section_code": "IT31S1", "instructor_id": 3 },
          { "id": 502, "subject_id": 101, "section_code": "IT31S2", "instructor_id": 13 }
        ],
        "enrolled_subjects": [
          { "id": 2001, "student_id": 5, "class_offering_id": 501, "status": "enrolled" },
          { "id": 2002, "student_id": 6, "class_offering_id": 501, "status": "enrolled" },
          { "id": 2003, "student_id": 7, "class_offering_id": 501, "status": "dropped" },
          { "id": 2004, "student_id": 7, "class_offering_id": 502, "status": "enrolled" }
        ],
        "attendance_logs": [],
        "assignments": [],
        "assignment_submissions": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CourseworkTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _json;
    private readonly CourseworkStore _work;
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow);

    public CourseworkStoreTests()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(path, Fixture);
        _json = new CanonicalJsonStore(path);
        _work = new CourseworkStore(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private static CourseworkStore.AssignmentInput Input(string title = "Lab 1", DateTime? due = null, bool publish = false, int offering = 501, decimal max = 50) =>
        new(null, offering, title, "Do the lab.", "Lab", due, max, publish);

    private JsonObject Row(string collection, int id) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), collection).OfType<JsonObject>().First(r => r["id"]!.GetValue<int>() == id);

    private void Submit(int assignmentId, int studentId, string text = "my answer") =>
        _json.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetArray(root, "assignment_submissions");
            rows.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(rows), ["assignment_id"] = assignmentId, ["student_id"] = studentId,
                ["text"] = text, ["submitted_at"] = DateTime.UtcNow.ToString("O"), ["is_late"] = false, ["status"] = "Submitted"
            });
        });

    // ── Attendance ──────────────────────────────────────────────────────────

    [Fact]
    public void Attendance_SheetListsOnlyEnrolledStudents_WithNoMarksYet()
    {
        var sheet = _work.GetAttendanceSheet(501, Day, 3);

        Assert.Equal(["Alyssa Mendoza", "Juan Dela Cruz"], sheet.Rows.Select(r => r.Name));
        Assert.All(sheet.Rows, r => Assert.Null(r.Status));
    }

    [Fact]
    public void Attendance_IsSaved_AndASecondSaveForTheSameDateReplacesIt()
    {
        Assert.Equal(2, _work.SaveAttendance(501, Day, " Intro ", new Dictionary<int, string> { [5] = "Present", [6] = "Absent" }, 3));
        _work.SaveAttendance(501, Day, "Intro", new Dictionary<int, string> { [5] = "Late", [6] = "Absent" }, 3);

        var sheet = _work.GetAttendanceSheet(501, Day, 3);
        Assert.Equal(["Late", "Absent"], sheet.Rows.Select(r => r.Status));
        Assert.Equal("Intro", sheet.Topic);
        Assert.Equal(2, CanonicalJsonStore.GetArray(_json.ReadRoot(), "attendance_logs").Count);
        Assert.Equal(3, Row("attendance_logs", 1)["recorded_by"]!.GetValue<int>());
    }

    [Fact]
    public void Attendance_Summary_CountsEachStatusAndTheAttendedRate()
    {
        _work.SaveAttendance(501, Day.AddDays(-2), null, new Dictionary<int, string> { [5] = "Present", [6] = "Absent" }, 3);
        _work.SaveAttendance(501, Day.AddDays(-1), null, new Dictionary<int, string> { [5] = "Late", [6] = "Excused" }, 3);
        _work.SaveAttendance(501, Day, null, new Dictionary<int, string> { [5] = "Absent", [6] = "Present" }, 3);

        var alyssa = _work.GetAttendanceSummary(501, 3).First(r => r.StudentId == 5);

        Assert.Equal((1, 1, 1, 0, 3), (alyssa.Present, alyssa.Late, alyssa.Absent, alyssa.Excused, alyssa.Sessions));
        Assert.Equal(66.7, alyssa.Rate);
    }

    [Fact]
    public void Attendance_RejectsFutureDates_BadStatuses_StrangersAndOtherInstructorsSections()
    {
        var ok = new Dictionary<int, string> { [5] = "Present" };

        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(501, Day.AddDays(5), null, ok, 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(501, Day, null, new Dictionary<int, string> { [5] = "Sleeping" }, 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(501, Day, null, new Dictionary<int, string> { [7] = "Present" }, 3)); // dropped
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(501, Day, null, ok, 13));   // someone else's section
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(501, Day, null, new Dictionary<int, string>(), 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.GetAttendanceSheet(501, Day, 13));
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAttendance(999, Day, null, ok, null));
        Assert.Empty(CanonicalJsonStore.GetArray(_json.ReadRoot(), "attendance_logs"));
        _work.SaveAttendance(501, Day, null, ok, null); // administrators may use any section
    }

    // ── Assignments ─────────────────────────────────────────────────────────

    [Fact]
    public void ADraft_IsCreated_AndInvisibleUntilPublished()
    {
        var id = _work.SaveAssignment(Input(), 3);

        var row = Assert.Single(_work.ListAssignments(3));
        Assert.Equal((id, "Draft", 50m, 2), (row.Id, row.Status, row.MaxScore, row.Enrolled));
        Assert.Equal("Lab", row.Type);
        Assert.Equal(3, Row("assignments", id)["created_by"]!.GetValue<int>());
        Assert.Empty(_work.ListAssignments(13)); // another instructor does not see it
    }

    [Fact]
    public void Publishing_NeedsADueDate_AndSetsThePublishedStamp()
    {
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAssignment(Input(publish: true), 3));
        var id = _work.SaveAssignment(Input(), 3);
        Assert.Throws<CourseworkRejectedException>(() => _work.SetStatus(id, CourseworkStore.Published, 3));

        _work.SaveAssignment(Input() with { Id = id, DueDate = DateTime.UtcNow.AddDays(3) }, 3);
        _work.SetStatus(id, CourseworkStore.Published, 3);

        Assert.Equal("Published", Row("assignments", id)["status"]!.GetValue<string>());
        Assert.NotNull(Row("assignments", id)["published_at"]);
    }

    [Fact]
    public void Close_Reopen_AndEditingRules()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(2), publish: true), 3);

        _work.SetStatus(id, CourseworkStore.Closed, 3);
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAssignment(Input("Changed") with { Id = id }, 3)); // closed
        _work.SetStatus(id, CourseworkStore.Published, 3);
        _work.SaveAssignment(Input("Changed", due: DateTime.UtcNow.AddDays(2)) with { Id = id }, 3);

        Assert.Equal("Changed", _work.GetAssignment(id, 3)!.Header.Title);
        Assert.Throws<CourseworkRejectedException>(() => _work.SetStatus(id, "Bogus", 3));
    }

    [Theory]
    [InlineData("", "Lab", 50)]
    [InlineData("T", "Nonsense", 50)]
    [InlineData("T", "Lab", 0)]
    [InlineData("T", "Lab", 5000)]
    public void BadAssignmentInput_IsRejected(string title, string type, int max)
    {
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAssignment(new(null, 501, title, "", type, null, max, false), 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAssignment(Input(offering: 502), 3)); // not their section
        Assert.Empty(CanonicalJsonStore.GetArray(_json.ReadRoot(), "assignments"));
    }

    [Fact]
    public void OnlyAnUnsubmittedDraft_CanBeDeleted()
    {
        var draft = _work.SaveAssignment(Input("Draft"), 3);
        var live = _work.SaveAssignment(Input("Live", due: DateTime.UtcNow.AddDays(1), publish: true), 3);

        Assert.Throws<CourseworkRejectedException>(() => _work.DeleteAssignment(live, 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.DeleteAssignment(draft, 13));
        _work.DeleteAssignment(draft, 3);

        Assert.Equal(live, Assert.Single(_work.ListAssignments(3)).Id);
    }

    [Fact]
    public void ADraftWithSubmissions_IsKept_AndCannotMoveSections()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(1), publish: true), 3);
        Submit(id, 5);
        _work.SetStatus(id, CourseworkStore.Closed, 3);
        _work.SetStatus(id, CourseworkStore.Published, 3);

        Assert.Throws<CourseworkRejectedException>(() => _work.SaveAssignment(Input(offering: 502) with { Id = id }, 3));
    }

    // ── Grading ─────────────────────────────────────────────────────────────

    [Fact]
    public void ASubmission_IsScoredWithinTheMaximum_AndKeepsItsFeedback()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(1), publish: true, max: 50), 3);
        Submit(id, 5);

        _work.GradeSubmission(1, 42.5m, " Good work ", 3);

        var submission = Assert.Single(_work.GetAssignment(id, 3)!.Submissions);
        Assert.Equal((42.5m, "Good work"), (submission.Score, submission.Feedback));
        Assert.NotNull(submission.GradedAt);
        Assert.Equal("Graded", Row("assignment_submissions", 1)["status"]!.GetValue<string>());
        Assert.Equal(1, _work.ListAssignments(3).First().Graded);
    }

    [Fact]
    public void FeedbackOnly_DoesNotCountAsGraded()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(1), publish: true), 3);
        Submit(id, 5);

        _work.GradeSubmission(1, null, "Please add references.", 3);

        var submission = Assert.Single(_work.GetAssignment(id, 3)!.Submissions);
        Assert.Null(submission.Score);
        Assert.Equal("Please add references.", submission.Feedback);
        Assert.Equal(0, _work.ListAssignments(3).First().Graded);
    }

    [Fact]
    public void Grading_RejectsOutOfRangeScores_AndOtherInstructors()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(1), publish: true, max: 50), 3);
        Submit(id, 5);

        Assert.Throws<CourseworkRejectedException>(() => _work.GradeSubmission(1, 51m, null, 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.GradeSubmission(1, -1m, null, 3));
        Assert.Throws<CourseworkRejectedException>(() => _work.GradeSubmission(1, 10m, null, 13));
        Assert.Throws<CourseworkRejectedException>(() => _work.GradeSubmission(99, 10m, null, 3));
        Assert.Null(_work.GetAssignment(id, 3)!.Submissions[0].Score);
    }

    [Fact]
    public void ADetailPage_IsHiddenFromOtherInstructors_AndFeedbackListsNewestFirst()
    {
        var id = _work.SaveAssignment(Input(due: DateTime.UtcNow.AddDays(1), publish: true), 3);
        Submit(id, 5, "first");
        Submit(id, 6, "second");

        Assert.Null(_work.GetAssignment(id, 13));
        Assert.Equal(["first", "second"], _work.GetAssignment(id, 3)!.Submissions.Select(s => s.Text).OrderBy(t => t == "first" ? 0 : 1));
        Assert.Equal(2, _work.RecentSubmissions(3).Count);
        Assert.Empty(_work.RecentSubmissions(13));
    }
}
