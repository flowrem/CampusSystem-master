using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Models;
using RegistrarMain.Services;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
namespace RegistrarMain.Pages;

public class DashboardModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CanonicalJsonStore _jsonStore;

    public DashboardModel(DatabaseService db, SettingsService settings, CanonicalJsonStore jsonStore)
    {
        _db = db;
        _settings = settings;
        _jsonStore = jsonStore;
    }

    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string CurrentSemester { get; set; } = "1st Semester";
    public bool EncodingOpen { get; set; } = true;
    public string EncodingDeadline { get; set; } = "2025-10-30";

    public int TotalStudents { get; set; }
    public int ActiveEnrollments { get; set; }
    public int GraduatingStudents { get; set; }
    public int PendingDocs { get; set; }
    public int PendingAddDrop { get; set; }
    public int PendingOverload { get; set; }
    public int PendingRevisions { get; set; }
    public int TotalPendingQueue => PendingDocs + PendingAddDrop + PendingOverload + PendingRevisions;
    public int ActiveOfferingsCount { get; set; }

    public Dictionary<string, int> EnrollmentByYear { get; set; } = new();
    public List<ProgramStat> ProgramDistribution { get; set; } = new();
    public List<ActivityLog> RecentActivity { get; set; } = new();
    public List<SystemStatus> SystemStatuses { get; set; } = new();

    public class ProgramStat
    {
        public string Program { get; set; } = "";
        public int Cnt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? switch_role)
    {
        if (switch_role == "registrar")
        {
            _db.SwitchToRegistrar();
            return RedirectToPage("/Dashboard");
        }

        _db.EnsureRegistrarSession();

        CurrentSchoolYear = await _settings.GetSchoolYearAsync();
        CurrentSemester = await _settings.GetSemesterAsync();
        EncodingOpen = await _settings.IsEncodingOpenAsync();
        EncodingDeadline = await _settings.GetEncodingDeadlineAsync();

        var root = _jsonStore.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var students = users.Where(item => JsonString(item, "role") == "student").ToList();
        TotalStudents = students.Count;
        ActiveEnrollments = enrollments.Count(item =>
            JsonString(item, "school_year") == CurrentSchoolYear && JsonString(item, "semester") == CurrentSemester &&
            new[] { "active", "registered", "enrolled" }.Contains(JsonString(item, "status"), StringComparer.OrdinalIgnoreCase));
        GraduatingStudents = profiles.Count(item => JsonString(item, "academic_status").Contains("Graduating", StringComparison.OrdinalIgnoreCase));
        PendingDocs = JsonCount(root, "transcript_requests", item => new[] { "pending", "processing" }.Contains(JsonString(item, "processing_status"), StringComparer.OrdinalIgnoreCase));
        PendingAddDrop = JsonCount(root, "add_drop_requests", item => JsonString(item, "status") == "pending");
        PendingOverload = JsonCount(root, "overload_waiver_requests", item => JsonString(item, "status") == "pending");
        PendingRevisions = JsonCount(root, "completion_revision_requests", item => JsonString(item, "status") == "pending");
        ActiveOfferingsCount = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().Count(item => JsonString(item, "status") == "open");

        EnrollmentByYear = enrollments.Where(item => new[] { "active", "registered", "enrolled" }.Contains(JsonString(item, "status"), StringComparer.OrdinalIgnoreCase))
            .GroupBy(item => JsonString(item, "school_year", "Unknown"))
            .ToDictionary(group => group.Key, group => group.Count());
        ProgramDistribution = profiles.GroupBy(item => JsonString(item, "program", "Unspecified"))
            .Select(group => new ProgramStat { Program = group.Key, Cnt = group.Count() })
            .OrderByDescending(item => item.Cnt)
            .ToList();

        RecentActivity = (root["unified_seed_data"]?["activity_log"] as JsonArray ?? new JsonArray())
            .OfType<JsonObject>()
            .OrderByDescending(item => DateTime.TryParse(JsonString(item, "created_at"), out var createdAt) ? createdAt : DateTime.MinValue)
            .Take(8)
            .Select(item => new ActivityLog
            {
                Id = JsonInt(item, "id"),
                UserId = item["user_id"]?.GetValue<int>(),
                Message = JsonString(item, "message"),
                CreatedAt = DateTime.TryParse(JsonString(item, "created_at"), out var createdAt) ? createdAt : DateTime.MinValue
            }).ToList();

        return Page();
    }

    private static string JsonString(JsonObject item, string property, string fallback = "") => item[property]?.GetValue<string>() ?? fallback;
    private static int JsonInt(JsonObject item, string property) => item[property]?.GetValue<int>() ?? 0;
    private static int JsonCount(JsonObject root, string collection, Func<JsonObject, bool> predicate) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Count(predicate);

    public async Task<IActionResult> OnPostSaveTermSettingsAsync(string school_year, string semester, string? encoding_open, string? deadline)
    {
        _db.EnsureRegistrarSession();

        var sy = (school_year ?? "").Trim();
        var sem = (semester ?? "").Trim();
        var enc = encoding_open == "1" ? "1" : "0";
        var dead = (deadline ?? "").Trim();

        if (!string.IsNullOrEmpty(sy) && !string.IsNullOrEmpty(sem))
        {
            await _db.SetSettingAsync("current_school_year", sy);
            await _db.SetSettingAsync("current_semester", sem);
            await _db.SetSettingAsync("grade_encoding_open", enc);
            await _db.SetSettingAsync("grade_encoding_deadline", dead);

            await _db.LogActivityAsync(_db.CurrentUserId,
                $"Academic term updated to AY {sy} {sem}. Grade encoding window: {(enc == "1" ? "OPEN" : "CLOSED")}");

            TempData["FlashMessage"] = "Academic Term settings updated successfully.";
            TempData["FlashType"] = "success";
        }

        return RedirectToPage("/Dashboard");
    }
}
