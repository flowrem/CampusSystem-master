using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using FacultyPortalMain.Services;

namespace FacultyPortalMain.Tests;

public sealed class FacultyJsonStoreTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "settings": [
          { "key": "current_school_year", "value": "2025-2026" },
          { "key": "current_semester", "value": "1st Semester" },
          { "key": "grade_encoding_open", "value": "1" }
        ],
        "users": [
          { "id": 5, "student_id_number": "2023-01045", "name": "Alyssa Mendoza", "role": "student" },
          { "id": 6, "student_id_number": "2023-01088", "name": "Juan Dela Cruz", "role": "student" },
          { "id": 7, "student_id_number": "2022-00512", "name": "Kathryn Bernardo", "role": "student" }
        ],
        "student_profiles": [ { "user_id": 5, "program": "BS Information Technology", "year_level": "3rd Year" } ],
        "subjects": [ { "id": 101, "subject_code": "IT311", "subject_name": "Web Systems", "units": 3 } ],
        "class_offerings": [
          { "id": 501, "subject_id": 101, "section_code": "IT31S1", "room": "CLAB-302", "days_of_week": "MW", "start_time": "08:00:00", "end_time": "10:30:00",
            "instructor_id": 3, "instructor_name": "Prof. Alan Turing", "school_year": "2025-2026", "semester": "1st Semester" },
          { "id": 599, "subject_id": 101, "section_code": "OLD", "school_year": "2024-2025", "semester": "1st Semester", "instructor_name": "Someone Else" }
        ],
        "enrolled_subjects": [
          { "id": 2001, "enrollment_id": 1001, "student_id": 5, "class_offering_id": 501, "subject_code": "IT311", "status": "enrolled" },
          { "id": 2002, "enrollment_id": 1002, "student_id": 6, "class_offering_id": 501, "subject_code": "IT311", "status": "enrolled" },
          { "id": 2003, "enrollment_id": 1003, "student_id": 7, "class_offering_id": 501, "subject_code": "IT311", "status": "dropped" }
        ],
        "grades": [
          { "id": 7001, "enrolled_subject_id": 2001, "student_id": 5, "subject_code": "IT311", "final_grade": 1.25, "is_inc": false, "remarks": "Passed" }
        ],
        "student_clearance": [
          { "id": 4001, "student_id": 5, "department_name": "Finance Office", "status": "Cleared" },
          { "id": 4002, "student_id": 6, "department_name": "Registrar Office", "status": "Cleared" }
        ],
        "activity_log": [],
        "guidance_requests": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FacultyJsonTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CanonicalJsonStore _json;
    private readonly FacultyJsonStore _faculty;

    public FacultyJsonStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(_path, Fixture);
        _json = new CanonicalJsonStore(_path);
        _faculty = new FacultyJsonStore(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private void SetSetting(string key, string value) => _json.UpdateRoot(root =>
        CanonicalJsonStore.GetArray(root, "settings").OfType<JsonObject>().First(s => s["key"]!.GetValue<string>() == key)["value"] = value);

    private JsonObject GradeRow(int enrolledSubjectId) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), "grades").OfType<JsonObject>()
            .First(g => g["enrolled_subject_id"]!.GetValue<int>() == enrolledSubjectId);

    [Fact]
    public void Offerings_AreCurrentTermOnly_WithSubjectDetailsAndLiveCount()
    {
        var offering = Assert.Single(_faculty.GetClassOfferings());

        Assert.Equal("IT31S1", offering.SectionCode);
        Assert.Equal("Web Systems", offering.SubjectName);
        Assert.Equal(3m, offering.Units);
        Assert.Equal(2, offering.EnrolledCount); // the dropped student is not counted
        Assert.Empty(_faculty.GetClassOfferings(99));
        Assert.Single(_faculty.GetClassOfferings(3));
    }

    [Fact]
    public void Offerings_FollowTheCurrentTermSetting()
    {
        SetSetting("current_school_year", "2024-2025");

        Assert.Equal("OLD", Assert.Single(_faculty.GetClassOfferings()).SectionCode);
    }

    [Fact]
    public void Roster_ShowsGradeAndFinanceClearance_AndSkipsDropped()
    {
        var roster = _faculty.GetClassRoster(501);

        Assert.Equal(["Alyssa Mendoza", "Juan Dela Cruz"], roster.Select(s => s.Name));
        var alyssa = roster[0];
        Assert.Equal("Cleared", alyssa.FinanceClearanceStatus);
        Assert.Equal(1.25m, alyssa.FinalGrade);
        Assert.Equal("locked", alyssa.GradeStatus); // no status field means it predates drafts
        Assert.True(alyssa.GradeIsFrozen);
        Assert.Equal("BS Information Technology", alyssa.Program);

        var juan = roster[1];
        Assert.Equal("NotCleared", juan.FinanceClearanceStatus); // only a Registrar row exists
        Assert.Null(juan.FinalGrade);
        Assert.Equal("", juan.GradeStatus);
        Assert.Empty(_faculty.GetClassRoster(12345));
    }

    [Fact]
    public void SaveGrade_CreatesDraft_WithTermInstructorAndLogEntry()
    {
        _faculty.SaveGrade(2002, 1.75m, " Strong finish ");

        var row = GradeRow(2002);
        Assert.Equal(1.75m, row["final_grade"]!.GetValue<decimal>());
        Assert.Equal("draft", row["status"]!.GetValue<string>());
        Assert.Equal("Passed", row["remarks"]!.GetValue<string>());
        Assert.Equal("Strong finish", row["faculty_note"]!.GetValue<string>());
        Assert.Equal("2025-2026", row["school_year"]!.GetValue<string>());
        Assert.Equal(3, row["encoded_by_faculty_id"]!.GetValue<int>());
        Assert.Equal(6, row["student_id"]!.GetValue<int>());
        Assert.Single(CanonicalJsonStore.GetArray(_json.ReadRoot(), "activity_log"));

        var roster = _faculty.GetClassRoster(501);
        Assert.Equal("draft", roster[1].GradeStatus);
        Assert.False(roster[1].GradeIsFrozen);
    }

    [Fact]
    public void SaveGrade_CanRewriteADraft_ButMarksFailing()
    {
        _faculty.SaveGrade(2002, 1.75m, null);
        _faculty.SaveGrade(2002, 5.00m, null);

        Assert.Equal(5.00m, GradeRow(2002)["final_grade"]!.GetValue<decimal>());
        Assert.Equal("Failed", GradeRow(2002)["remarks"]!.GetValue<string>());
        Assert.Equal(2, CanonicalJsonStore.GetArray(_json.ReadRoot(), "grades").Count);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("verified")]
    public void SaveGrade_RefusesFinalGrades(string status)
    {
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "grades")[0]!["status"] = status);

        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2001, 2.00m, null));
        Assert.Equal(1.25m, GradeRow(2001)["final_grade"]!.GetValue<decimal>());
    }

    [Fact]
    public void SaveGrade_RefusesLegacyGradeWithoutStatus()
    {
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2001, 2.00m, null));
    }

    [Fact]
    public void SaveGrade_Refuses_ClosedWindow_BadRange_AndDroppedOrUnknownEnrollment()
    {
        var before = File.ReadAllText(_path);

        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2002, 0.99m, null));
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2002, 5.01m, null));
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2003, 2.00m, null));
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(9999, 2.00m, null));
        Assert.Equal(before, File.ReadAllText(_path));

        SetSetting("grade_encoding_open", "0");
        Assert.False(_faculty.IsEncodingOpen());
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2002, 2.00m, null));
    }

    private JsonArray Referrals() => CanonicalJsonStore.GetArray(_json.ReadRoot(), "guidance_requests");

    [Fact]
    public void FailingGrade_OpensOneGuidanceReferral_ThatReGradingDoesNotDuplicate()
    {
        _faculty.SaveGrade(2002, 5.00m, null);
        _faculty.SaveGrade(2002, 4.00m, null);

        var referral = Assert.IsType<JsonObject>(Assert.Single(Referrals()));
        Assert.Equal(6, referral["student_id"]!.GetValue<int>());
        Assert.Equal("ReferredIn", referral["status"]!.GetValue<string>());
        Assert.Equal(1, referral["id"]!.GetValue<int>());
        Assert.Equal(2002, referral["enrolled_subject_id"]!.GetValue<int>());
        Assert.Contains("failing grade 5.00", referral["details"]!.GetValue<string>());
    }

    [Fact]
    public void CounselorTriage_OfTheReferral_SurvivesAReGrade()
    {
        _faculty.SaveGrade(2002, 5.00m, null);
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "guidance_requests")[0]!["status"] = "InProgress");

        _faculty.SaveGrade(2002, 4.50m, null);

        Assert.Equal("InProgress", Referrals()[0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void PassingGrade_OpensNoReferral()
    {
        _faculty.SaveGrade(2002, 3.00m, null);

        Assert.Empty(Referrals());
    }

    [Fact]
    public void Incomplete_IsADraftWithAReason_AndOpensAReferral()
    {
        _faculty.SaveIncomplete(2002, " Missing project ");

        var row = GradeRow(2002);
        Assert.True(row["is_inc"]!.GetValue<bool>());
        Assert.Null(row["final_grade"]);
        Assert.Equal("Missing project", row["inc_reason"]!.GetValue<string>());
        Assert.Equal("draft", row["status"]!.GetValue<string>());
        Assert.Single(Referrals());
        var roster = _faculty.GetClassRoster(501);
        Assert.True(roster[1].IsInc);
        Assert.Null(roster[1].FinalGrade);
    }

    [Fact]
    public void Incomplete_IsRefused_ForFinalGradesAndClosedWindow()
    {
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveIncomplete(2001, null));
        SetSetting("grade_encoding_open", "0");
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveIncomplete(2002, null));
        Assert.Empty(Referrals());
    }

    [Fact]
    public void AnInstructor_SeesOnlyTheirOwnSections_AndCannotGradeOthers()
    {
        Assert.Equal(2, _faculty.GetClassRoster(501, 3).Count);
        Assert.Empty(_faculty.GetClassRoster(501, 99));

        _faculty.SaveGrade(2002, 2.00m, null, 3);
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveGrade(2002, 1.00m, null, 99));
        Assert.Throws<GradeRejectedException>(() => _faculty.SaveIncomplete(2002, null, 99));
        Assert.Equal(2.00m, GradeRow(2002)["final_grade"]!.GetValue<decimal>());
    }

    [Fact]
    public void RepositoryData_IsReadableByEveryFacultyQuery()
    {
        var faculty = new FacultyJsonStore(new CanonicalJsonStore(CanonicalJsonStore.ResolvePath()));

        var offerings = faculty.GetClassOfferings();
        Assert.NotEmpty(offerings);
        Assert.NotEmpty(faculty.GetClassRoster(offerings[0].Id));
    }
}
