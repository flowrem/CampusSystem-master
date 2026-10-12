using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using RegistrarMain.Contracts;
using RegistrarMain.Services;

namespace RegistrarMain.Tests;

public sealed class RegistrarApiStoreTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "users": [
          { "id": 5, "student_id_number": "2023-01045", "name": "Alyssa", "role": "student" },
          { "id": 1, "student_id_number": "REG-1", "name": "Registrar", "role": "registrar" }
        ],
        "subjects": [
          { "id": 101, "subject_code": "IT311", "subject_name": "Web Systems", "units": 3 },
          { "id": 102, "subject_code": "IT312", "subject_name": "Databases", "units": 3 },
          { "id": 103, "subject_code": "IT313", "subject_name": "Security", "units": 3 }
        ],
        "enrollments": [
          { "id": 1001, "student_id": 5, "school_year": "2025-2026", "semester": "1st Semester", "total_units": 3 }
        ],
        "class_offerings": [
          { "id": 501, "subject_id": 101, "school_year": "2025-2026", "semester": "1st Semester", "enrolled_count": 1, "max_capacity": 40 },
          { "id": 502, "subject_id": 102, "school_year": "2025-2026", "semester": "1st Semester", "enrolled_count": 1, "max_capacity": 1 }
        ],
        "enrolled_subjects": [
          { "id": 2001, "enrollment_id": 1001, "student_id": 5, "class_offering_id": 501, "subject_code": "IT311", "subject_name": "Web Systems", "units": 3, "status": "enrolled" }
        ],
        "grades": [
          { "id": 7001, "student_id": 5, "subject_code": "IT311", "school_year": "2025-2026", "semester": "1st Semester", "final_grade": 1.25, "is_inc": false },
          { "id": 7002, "student_id": 5, "subject_code": "IT312", "school_year": "2025-2026", "semester": "1st Semester", "final_grade": null, "is_inc": true },
          { "id": 7003, "student_id": 5, "subject_code": "IT313", "school_year": "2025-2026", "semester": "1st Semester", "final_grade": null, "is_inc": false }
        ],
        "transcript_requests": [
          { "id": 6001, "student_id": 5, "document_type": "Official Transcript of Records (TOR)", "processing_status": "Ready for Release", "request_date": "2025-09-10T10:15:00Z" }
        ]
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RegistrarApiTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly RegistrarApiStore _api;

    public RegistrarApiStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(_path, Fixture);
        _api = new RegistrarApiStore(new CanonicalJsonStore(_path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public void StudentExists_OnlyForStudentRole()
    {
        Assert.True(_api.StudentExists(5));
        Assert.False(_api.StudentExists(1));
        Assert.False(_api.StudentExists(99));
    }

    [Fact]
    public void Courses_AreSubjects_SearchableByCodeOrTitle()
    {
        Assert.Equal(3, _api.GetCourses(null).Count);
        Assert.Equal("IT312", Assert.Single(_api.GetCourses("databases")).Code);
        Assert.Equal(3, _api.GetCourse(101)!.Credits);
        Assert.Null(_api.GetCourse(999));
    }

    [Fact]
    public void CreateCourse_AddsSubject_AndRejectsDuplicateCode()
    {
        var created = _api.CreateCourse(new CreateCourseRequest(" it400 ", " Capstone ", 6));

        Assert.Equal("IT400", created.Code);
        Assert.Equal(6, created.Credits);
        Assert.Equal(104, created.Id);
        Assert.Equal("Capstone", _api.GetCourse(104)!.Title);
        var ex = Assert.Throws<RegistrarApiException>(() => _api.CreateCourse(new CreateCourseRequest("IT400", "Again", 3)));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Registrations_ListsCurrentSubjects_WithTermLabel()
    {
        var mine = Assert.Single(_api.GetRegistrations(5));

        Assert.Equal("IT311", mine.CourseCode);
        Assert.Equal(101, mine.CourseId);
        Assert.Equal("2025-2026 1st Semester", mine.Semester);
        Assert.Empty(_api.GetRegistrations(1));
    }

    [Fact]
    public void Register_EnrollsInOpenSection_AndUpdatesCounts()
    {
        // The only IT313 offering does not exist, IT312 is full, so add an open one.
        var ex = Assert.Throws<RegistrarApiException>(() => _api.Register(5, new EnrollmentRequestDto(103, "1st Semester")));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(409, Assert.Throws<RegistrarApiException>(() => _api.Register(5, new EnrollmentRequestDto(102, "1st Semester"))).StatusCode);

        var json = new CanonicalJsonStore(_path);
        json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "class_offerings").Add(new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = 503, ["subject_id"] = 103, ["school_year"] = "2025-2026", ["semester"] = "1st Semester", ["enrolled_count"] = 0, ["max_capacity"] = 30
        }));

        var registered = _api.Register(5, new EnrollmentRequestDto(103, "2025-2026 1st Semester"));

        Assert.Equal("IT313", registered.CourseCode);
        Assert.Equal(2, _api.GetRegistrations(5).Count);
        var root = json.ReadRoot();
        Assert.Equal(1, CanonicalJsonStore.GetArray(root, "class_offerings").OfType<System.Text.Json.Nodes.JsonObject>().First(o => o["id"]!.GetValue<int>() == 503)["enrolled_count"]!.GetValue<int>());
        Assert.Equal(6, CanonicalJsonStore.GetArray(root, "enrollments")[0]!["total_units"]!.GetValue<decimal>());
        Assert.Equal(409, Assert.Throws<RegistrarApiException>(() => _api.Register(5, new EnrollmentRequestDto(103, "1st Semester"))).StatusCode);
    }

    [Fact]
    public void Register_And_Drop_KeepAnAssessedStudentsFeesInStep()
    {
        var json = new CanonicalJsonStore(_path);
        json.UpdateRoot(root =>
        {
            var data = (JsonObject)root["unified_seed_data"]!;
            data["student_profiles"] = new JsonArray(new JsonObject { ["user_id"] = 5, ["program"] = "BS Information Technology" });
            data["tuition_fee_matrix"] = new JsonArray(new JsonObject
            {
                ["id"] = 1, ["program_code"] = "BSIT", ["academic_year"] = "2025-2026", ["tuition_per_unit"] = 1000,
                ["base_miscellaneous_fee"] = 100, ["energy_it_fee"] = 0, ["library_fee"] = 0, ["registration_fee"] = 0
            });
            data["fee_assessments"] = new JsonArray(new JsonObject
            {
                ["id"] = 1, ["enrollment_id"] = 1001, ["student_id"] = 5, ["school_year"] = "2025-2026", ["tuition_fee"] = 3000,
                ["miscellaneous_fees"] = 100, ["laboratory_fees"] = 0, ["discounts_or_scholarship"] = 0,
                ["total_net_assessment"] = 3100, ["total_paid"] = 0, ["remaining_balance"] = 3100, ["status"] = "Pending"
            });
            CanonicalJsonStore.GetArray(root, "class_offerings").Add(new JsonObject
            {
                ["id"] = 503, ["subject_id"] = 103, ["school_year"] = "2025-2026", ["semester"] = "1st Semester", ["enrolled_count"] = 0, ["max_capacity"] = 30
            });
        });

        var registered = _api.Register(5, new EnrollmentRequestDto(103, "1st Semester"));
        decimal Total() => CanonicalJsonStore.GetArray(json.ReadRoot(), "fee_assessments")[0]!["total_net_assessment"]!.GetValue<decimal>();
        Assert.Equal(6100m, Total());
        Assert.Single(CanonicalJsonStore.GetArray(json.ReadRoot(), "fee_adjustments"));

        _api.Drop(5, registered.Id, registered.RowVersion);
        Assert.Equal(3100m, Total());
        Assert.Equal(2, CanonicalJsonStore.GetArray(json.ReadRoot(), "fee_adjustments").Count);
    }

    [Fact]
    public void Register_Rejects_UnknownCourse_AndTermWithoutEnrollment()
    {
        Assert.Equal(404, Assert.Throws<RegistrarApiException>(() => _api.Register(5, new EnrollmentRequestDto(999, "1st Semester"))).StatusCode);
        Assert.Equal(404, Assert.Throws<RegistrarApiException>(() => _api.Register(5, new EnrollmentRequestDto(102, "2nd Semester"))).StatusCode);
    }

    [Fact]
    public void Drop_RequiresMatchingVersion_ThenMarksRowDropped()
    {
        var registration = Assert.Single(_api.GetRegistrations(5));

        Assert.Equal(409, Assert.Throws<RegistrarApiException>(() => _api.Drop(5, registration.Id, "stale")).StatusCode);
        Assert.Equal(409, Assert.Throws<RegistrarApiException>(() => _api.Drop(8, registration.Id, registration.RowVersion)).StatusCode);

        _api.Drop(5, registration.Id, registration.RowVersion);

        Assert.Empty(_api.GetRegistrations(5));
        Assert.Equal(409, Assert.Throws<RegistrarApiException>(() => _api.Drop(5, registration.Id, registration.RowVersion)).StatusCode);
        var root = new CanonicalJsonStore(_path).ReadRoot();
        Assert.Equal(0, CanonicalJsonStore.GetArray(root, "enrollments")[0]!["total_units"]!.GetValue<decimal>());
        Assert.Equal(0, CanonicalJsonStore.GetArray(root, "class_offerings")[0]!["enrolled_count"]!.GetValue<int>());
    }

    [Fact]
    public void RowVersion_ChangesWhenTheRowChanges()
    {
        var before = Assert.Single(_api.GetRegistrations(5)).RowVersion;
        new CanonicalJsonStore(_path).UpdateRoot(root =>
            CanonicalJsonStore.GetArray(root, "enrolled_subjects")[0]!["status"] = "enrolled-late");

        Assert.NotEqual(before, Assert.Single(_api.GetRegistrations(5)).RowVersion);
    }

    [Fact]
    public void Transcript_GroupsGradedSubjectsByTerm_AndShowsInc()
    {
        var semester = Assert.Single(_api.GetTranscript(5).Semesters);

        Assert.Equal("2025-2026 1st Semester", semester.Semester);
        Assert.Equal(["IT311", "IT312"], semester.Entries.Select(e => e.CourseCode));
        Assert.Equal(["1.25", "INC"], semester.Entries.Select(e => e.Grade));
        Assert.Empty(_api.GetTranscript(1).Semesters);
    }

    [Fact]
    public void Transcript_HidesDraftGrades()
    {
        new CanonicalJsonStore(_path).UpdateRoot(root =>
            CanonicalJsonStore.GetArray(root, "grades")[0]!["status"] = "draft");

        Assert.Equal(["IT312"], Assert.Single(_api.GetTranscript(5).Semesters).Entries.Select(e => e.CourseCode));
    }

    [Fact]
    public void RecordsRequests_AreRowsRegistrarSees_AndExcludeVerifications()
    {
        var created = _api.CreateRecordsRequest(5, " Certificate of Grades ");
        _api.CreateVerification(5, "Visa");

        var records = _api.GetRecordsRequests(5);
        Assert.Equal(2, records.Count);
        Assert.Equal("Certificate of Grades", records[0].DocumentType);
        Assert.Equal("Pending", created.Status);

        var verification = Assert.Single(_api.GetVerifications(5));
        Assert.Equal("Pending", verification.Status);

        var row = CanonicalJsonStore.GetArray(new CanonicalJsonStore(_path).ReadRoot(), "transcript_requests")
            .OfType<System.Text.Json.Nodes.JsonObject>().First(r => r["id"]!.GetValue<int>() == verification.Id);
        Assert.Equal("Enrollment Verification", row["document_type"]!.GetValue<string>());
        Assert.Equal("Visa", row["purpose"]!.GetValue<string>());
        Assert.StartsWith("NU-ENR-", row["tracking_token"]!.GetValue<string>());
    }

    [Fact]
    public void RepositoryData_IsReadableByEveryApiQuery()
    {
        var api = new RegistrarApiStore(new CanonicalJsonStore(CanonicalJsonStore.ResolvePath()));

        Assert.NotEmpty(api.GetCourses(null));
        Assert.NotEmpty(api.GetRegistrations(5));
        Assert.NotEmpty(api.GetTranscript(5).Semesters);
        Assert.NotEmpty(api.GetRecordsRequests(5));
    }
}
