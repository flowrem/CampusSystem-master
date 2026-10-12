using System.Text.Json.Nodes;
using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Http;
using StudentPortalMain.Services;

namespace StudentPortalMain.Tests;

public sealed class CourseworkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PortalCourseworkTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _json;
    private readonly StudentPortalDbService _portal;

    public CourseworkTests()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        var due = DateTime.UtcNow.AddDays(2).ToString("O");
        File.WriteAllText(path, new JsonObject
        {
            ["system"] = new JsonObject { ["schema_version"] = 1 },
            ["unified_seed_data"] = new JsonObject
            {
                ["users"] = new JsonArray
                {
                    new JsonObject { ["id"] = 5, ["student_id_number"] = "2023-01045", ["name"] = "Alyssa", ["role"] = "student", ["status"] = "active", ["password_hash"] = AuthStore.Hash("Student#123") },
                    new JsonObject { ["id"] = 6, ["student_id_number"] = "2023-01088", ["name"] = "Juan", ["role"] = "student", ["status"] = "active", ["password_hash"] = AuthStore.Hash("Student#456") },
                },
                ["subjects"] = new JsonArray(new JsonObject { ["id"] = 101, ["subject_code"] = "IT311", ["subject_name"] = "Web Systems" }),
                ["class_offerings"] = new JsonArray(
                    new JsonObject { ["id"] = 501, ["subject_id"] = 101, ["section_code"] = "S1" },
                    new JsonObject { ["id"] = 502, ["subject_id"] = 101, ["section_code"] = "S2" }),
                ["enrolled_subjects"] = new JsonArray(
                    new JsonObject { ["id"] = 1, ["student_id"] = 5, ["class_offering_id"] = 501, ["status"] = "enrolled" },
                    new JsonObject { ["id"] = 2, ["student_id"] = 6, ["class_offering_id"] = 502, ["status"] = "enrolled" }),
                ["assignments"] = new JsonArray(
                    new JsonObject { ["id"] = 1, ["class_offering_id"] = 501, ["title"] = "Lab 1", ["instructions"] = "Do it", ["type"] = "Lab", ["max_score"] = 50, ["due_date"] = due, ["status"] = "Published" },
                    new JsonObject { ["id"] = 2, ["class_offering_id"] = 501, ["title"] = "Hidden draft", ["max_score"] = 10, ["status"] = "Draft" },
                    new JsonObject { ["id"] = 3, ["class_offering_id"] = 501, ["title"] = "Old quiz", ["max_score"] = 20, ["due_date"] = due, ["status"] = "Closed" },
                    new JsonObject { ["id"] = 4, ["class_offering_id"] = 502, ["title"] = "Other section", ["max_score"] = 20, ["due_date"] = due, ["status"] = "Published" }),
                ["assignment_submissions"] = new JsonArray(),
                ["activity_log"] = new JsonArray(),
            }
        }.ToJsonString());
        _json = new CanonicalJsonStore(path);
        _portal = new StudentPortalDbService(new HttpContextAccessor(), _json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private JsonObject Submission(int id) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), "assignment_submissions").OfType<JsonObject>().First(s => s["id"]!.GetValue<int>() == id);

    [Fact]
    public async Task AStudent_SeesPublishedAndClosedAssignments_OfTheirOwnSectionsOnly()
    {
        var items = await _portal.GetAssignmentsAsync(5);

        Assert.Equal(["Lab 1", "Old quiz"], items.Select(i => i.Title).OrderBy(t => t));
        var lab = items.First(i => i.Id == 1);
        Assert.Equal(("IT311", "Lab", "Do it"), (lab.SubjectCode, lab.Type, lab.Description));
        Assert.True(items.First(i => i.Id == 3).IsClosed);
        Assert.False(lab.IsSubmitted);
    }

    [Fact]
    public async Task TurningIn_SavesTheAnswer_AndShowsItBack()
    {
        Assert.Null(await _portal.SubmitAssignmentWorkAsync(1, 5, "  my answer "));

        var row = Submission(1);
        Assert.Equal(("my answer", 5, 1, false), (row["text"]!.GetValue<string>(), row["student_id"]!.GetValue<int>(), row["assignment_id"]!.GetValue<int>(), row["is_late"]!.GetValue<bool>()));
        var item = (await _portal.GetAssignmentsAsync(5)).First(i => i.Id == 1);
        Assert.True(item.IsSubmitted);
        Assert.Equal("my answer", item.SubmissionText);
        Assert.Single(CanonicalJsonStore.GetArray(_json.ReadRoot(), "activity_log"));
    }

    [Fact]
    public async Task TurningInAgain_BeforeGrading_UpdatesTheSameSubmission()
    {
        await _portal.SubmitAssignmentWorkAsync(1, 5, "first");
        await _portal.SubmitAssignmentWorkAsync(1, 5, "second");

        Assert.Single(CanonicalJsonStore.GetArray(_json.ReadRoot(), "assignment_submissions"));
        Assert.Equal("second", Submission(1)["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task AScoredSubmission_CanNoLongerBeChanged_AndShowsScoreAndFeedback()
    {
        await _portal.SubmitAssignmentWorkAsync(1, 5, "first");
        _json.UpdateRoot(root =>
        {
            var row = CanonicalJsonStore.GetArray(root, "assignment_submissions").OfType<JsonObject>().First();
            row["score"] = 44.5m;
            row["feedback"] = "Nice";
        });

        var error = await _portal.SubmitAssignmentWorkAsync(1, 5, "sneaky edit");

        Assert.Contains("already scored", error);
        Assert.Equal("first", Submission(1)["text"]!.GetValue<string>());
        var item = (await _portal.GetAssignmentsAsync(5)).First(i => i.Id == 1);
        Assert.Equal((44.5m, "Nice"), (item.MyScore, item.Feedback));
    }

    [Fact]
    public async Task ALateAnswer_IsAcceptedButFlagged()
    {
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "assignments").OfType<JsonObject>().First()["due_date"] = DateTime.UtcNow.AddHours(-3).ToString("O"));

        Assert.Null(await _portal.SubmitAssignmentWorkAsync(1, 5, "late work"));

        Assert.True(Submission(1)["is_late"]!.GetValue<bool>());
        Assert.True((await _portal.GetAssignmentsAsync(5)).First(i => i.Id == 1).IsLate);
    }

    [Theory]
    [InlineData(2, 5, "x")]      // a draft is not visible
    [InlineData(3, 5, "x")]      // closed
    [InlineData(4, 5, "x")]      // another section
    [InlineData(99, 5, "x")]     // unknown
    [InlineData(1, 6, "x")]      // student 6 is not in section 501
    public async Task Refused_WhenNotAvailableToTheStudent(int assignmentId, int studentId, string answer)
    {
        var error = await _portal.SubmitAssignmentWorkAsync(assignmentId, studentId, answer);

        Assert.NotNull(error);
        Assert.Empty(CanonicalJsonStore.GetArray(_json.ReadRoot(), "assignment_submissions"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AnEmptyAnswer_IsRefused(string? answer) =>
        Assert.Contains("Write your answer", await _portal.SubmitAssignmentWorkAsync(1, 5, answer));

    [Fact]
    public async Task AnAnswerOverTheLimit_IsRefused() =>
        Assert.Contains("too long", await _portal.SubmitAssignmentWorkAsync(1, 5, new string('x', 10_001)));

    // ── Sign-in ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_UsesTheSharedPasswords_AndLocksAfterFiveMisses()
    {
        Assert.NotNull(await _portal.AuthenticateStudentAsync("2023-01045", "Student#123"));
        Assert.Null(await _portal.AuthenticateStudentAsync("2023-01045", "Student#456")); // another student's password

        for (var i = 0; i < AuthStore.MaxFailures; i++) await _portal.AuthenticateStudentAsync("2023-01045", "bad");

        Assert.Null(await _portal.AuthenticateStudentAsync("2023-01045", "Student#123"));
        Assert.Contains("Too many failed attempts", _portal.LastLoginError);
    }
}
