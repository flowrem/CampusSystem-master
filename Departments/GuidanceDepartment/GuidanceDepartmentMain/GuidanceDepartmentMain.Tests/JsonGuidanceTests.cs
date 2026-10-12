using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CampusSystem.Data.Json;
using GuidanceDepartmentMain.Contracts;
using GuidanceDepartmentMain.Services;
using Xunit;

namespace GuidanceDepartmentMain.Tests;

public sealed class JsonGuidanceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuidanceJsonTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _store;
    private readonly JsonStudentDirectory _students;
    private readonly JsonGuidanceRequestStore _requests;
    private readonly GuidanceOperationsStore _operations;
    private readonly JsonAuditLogService _audit;

    public JsonGuidanceTests()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(path, new JsonObject
        {
            ["system"] = new JsonObject { ["schema_version"] = CanonicalJsonStore.CurrentSchemaVersion },
            ["unified_seed_data"] = new JsonObject
            {
                ["users"] = new JsonArray { User(5, "student"), User(9, "guidance"), User(12, "faculty") },
                ["guidance_requests"] = new JsonArray(), ["counseling_case_notes"] = new JsonArray(), ["guidance_appointments"] = new JsonArray(),
                ["student_clearance"] = new JsonArray { new JsonObject { ["id"] = 4003, ["student_id"] = 5, ["department_name"] = "Guidance Office", ["school_year"] = "2025-2026", ["semester"] = "1st Semester", ["status"] = "Pending" } },
                ["audit_events"] = new JsonArray(), ["refresh_tokens"] = new JsonArray()
            }
        }.ToJsonString());
        _store = new CanonicalJsonStore(path); _students = new JsonStudentDirectory(_store); _requests = new JsonGuidanceRequestStore(_store); _operations = new GuidanceOperationsStore(_store); _audit = new JsonAuditLogService(_store);
    }

    [Fact] public void StudentDirectory_lists_only_students() => Assert.Single(_students.List());
    [Fact] public void StudentDirectory_checks_existence() { Assert.True(_students.Exists(5)); Assert.False(_students.Exists(9)); }

    [Theory]
    [InlineData(0)] [InlineData(9)] [InlineData(12)] [InlineData(99)]
    public void StudentDirectory_refuses_non_students(int id) => Assert.False(_students.Exists(id));

    [Fact]
    public async Task RequestStore_assigns_int_id_and_persists_text_enums()
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help", Urgency = RequestUrgency.Priority, Status = RequestStatus.Requested };
        await _requests.AddAsync(item, default);
        var row = CanonicalJsonStore.GetArray(_store.ReadRoot(), "guidance_requests")[0]!.AsObject();
        Assert.Equal(1, item.Id); Assert.Equal("Priority", row["urgency"]!.GetValue<string>()); Assert.Equal("Requested", row["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task RequestStore_rejects_stale_row_version()
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help" }; await _requests.AddAsync(item, default);
        await _requests.SaveAsync(item, item.RowVersion, default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _requests.SaveAsync(item, [1], default));
    }

    [Fact]
    public async Task RequestStore_deletes_only_the_owner_row()
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help" }; await _requests.AddAsync(item, default);
        Assert.False(await _requests.DeleteAsync(item.Id, 7, item.RowVersion, default));
        Assert.True(await _requests.DeleteAsync(item.Id, 5, item.RowVersion, default));
    }

    [Fact]
    public async Task Referral_requires_real_student_and_creates_referred_in_case()
    {
        var triage = new CounselorTriageService(_requests, _audit, _students);
        Assert.False((await triage.ProcessIncomingReferralAsync(new(77, "Failing grade", "FAC"), default)).Succeeded);
        var result = await triage.ProcessIncomingReferralAsync(new(5, "Failing grade", "FAC"), default);
        Assert.True(result.Succeeded); Assert.Equal(RequestStatus.ReferredIn, result.Value!.Status);
    }

    [Fact]
    public async Task Triage_transition_writes_audit_event()
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help" }; await _requests.AddAsync(item, default);
        var result = await new CounselorTriageService(_requests, _audit, _students).TransitionAsync("9", new(item.Id, RequestStatus.InProgress, item.RowVersion), default);
        Assert.True(result.Succeeded); Assert.Single(await _audit.QueryAsync("GuidanceRequest", null, default));
    }

    [Theory]
    [InlineData(RequestStatus.Requested, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Requested, RequestStatus.ReferredOut)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Resolved)]
    [InlineData(RequestStatus.InProgress, RequestStatus.ReferredOut)]
    [InlineData(RequestStatus.ReferredOut, RequestStatus.ReferredIn)]
    [InlineData(RequestStatus.ReferredOut, RequestStatus.Resolved)]
    [InlineData(RequestStatus.ReferredIn, RequestStatus.InProgress)]
    [InlineData(RequestStatus.ReferredIn, RequestStatus.Resolved)]
    public async Task Triage_allows_supported_transitions(RequestStatus current, RequestStatus next)
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help", Status = current }; await _requests.AddAsync(item, default);
        Assert.True((await new CounselorTriageService(_requests, _audit, _students).TransitionAsync("9", new(item.Id, next, item.RowVersion), default)).Succeeded);
    }

    [Theory]
    [InlineData(RequestStatus.Requested, RequestStatus.Resolved)]
    [InlineData(RequestStatus.Resolved, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Resolved, RequestStatus.ReferredOut)]
    [InlineData(RequestStatus.InProgress, RequestStatus.ReferredIn)]
    public async Task Triage_rejects_unsupported_transitions(RequestStatus current, RequestStatus next)
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help", Status = current }; await _requests.AddAsync(item, default);
        Assert.Equal("invalid_transition", (await new CounselorTriageService(_requests, _audit, _students).TransitionAsync("9", new(item.Id, next, item.RowVersion), default)).ErrorCode);
    }

    [Theory]
    [InlineData(RequestUrgency.Normal)] [InlineData(RequestUrgency.Priority)] [InlineData(RequestUrgency.Urgent)]
    public async Task RequestStore_round_trips_every_urgency(RequestUrgency urgency)
    {
        var item = new GuidanceRequestRecord { StudentId = 5, Subject = "Support", Details = "Need help", Urgency = urgency }; await _requests.AddAsync(item, default);
        Assert.Equal(urgency, (await _requests.FindAsync(item.Id, default))!.Urgency);
    }

    [Fact]
    public void Operations_store_notes_and_appointments()
    {
        _operations.AddNote(new(5, "Academic", "Follow up Friday"), "Ms. Counselor");
        _operations.AddAppointment(new(5, DateTimeOffset.UtcNow.AddDays(1), "Academic advising", null), "Ms. Counselor");
        Assert.Single(_operations.ListNotes(5)); Assert.Single(_operations.ListAppointments(5));
    }

    [Fact]
    public void Clearance_updates_existing_guidance_row_without_duplication()
    {
        _operations.SignClearance(new(5, "2025-2026", "1st Semester", "Cleared", "Reviewed"), "Ms. Counselor");
        var rows = CanonicalJsonStore.GetArray(_store.ReadRoot(), "student_clearance").OfType<JsonObject>().Where(row => row["student_id"]!.GetValue<int>() == 5 && row["department_name"]!.GetValue<string>().Contains("Guidance")).ToList();
        Assert.Single(rows); Assert.Equal("Cleared", rows[0]["status"]!.GetValue<string>()); Assert.Equal("Ms. Counselor", rows[0]["cleared_by"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Pending")] [InlineData("Hold")] [InlineData("Cleared")]
    public void Clearance_preserves_the_requested_status(string status)
    {
        _operations.SignClearance(new(5, "2025-2026", "1st Semester", status, null), "Ms. Counselor");
        Assert.Equal(status, CanonicalJsonStore.GetArray(_store.ReadRoot(), "student_clearance")[0]!["status"]!.GetValue<string>());
    }

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
    private static JsonObject User(int id, string role) => new() { ["id"] = id, ["role"] = role, ["student_id_number"] = $"ID-{id}", ["name"] = $"User {id}", ["email"] = $"{id}@example.test" };
}
