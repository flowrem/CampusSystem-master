using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class GraduationAuditModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CanonicalJsonStore _store;

    public GraduationAuditModel(DatabaseService db, SettingsService settings, CanonicalJsonStore store)
    {
        _db = db;
        _settings = settings;
        _store = store;
    }

    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string ActiveTab { get; set; } = "candidates";
    public List<CandidateAuditItem> CandidateAudit { get; set; } = new();
    public List<CandidateAuditItem> HonorsList { get; set; } = new();

    public class CandidateAuditItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public decimal? AverageGrade { get; set; }
        public int CompletedUnits { get; set; }
        public int UnitsRemaining { get; set; }
        public string AcademicStatus { get; set; } = "";
        public string GuidanceClearanceStatus { get; set; } = "";
        public int UnverifiedDocsCount { get; set; }
        public int HasNstp { get; set; }
        public int AcademicDeficienciesCount { get; set; }
        public bool IsEligible { get; set; }
        public List<string> Deficiencies { get; set; } = new();
        public string? HonorTitle { get; set; }
        public string HonorBadge { get; set; } = "badge-on-leave";
    }

    public async Task<IActionResult> OnGetAsync(string tab = "candidates")
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "candidates", "honors", "print_roll" }.Contains(tab) ? tab : "candidates";
        CurrentSchoolYear = await _settings.GetSchoolYearAsync();

        var root = _store.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().Where(item => StringValue(item, "role") == "student").ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var documents = CanonicalJsonStore.GetArray(root, "document_credentials").OfType<JsonObject>().ToList();
        var nstpRecords = root["unified_seed_data"]?["nstp_serial_numbers"] as JsonArray ?? new JsonArray();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();

        foreach (var user in users)
        {
            var studentId = IntValue(user, "id");
            var profile = profiles.FirstOrDefault(item => IntValue(item, "user_id") == studentId);
            if (profile == null) continue;
            var yearLevel = StringValue(profile, "year_level");
            var academicStatus = StringValue(profile, "academic_status");
            if (yearLevel != "4th Year" && academicStatus is not ("Graduating" or "Graduated")) continue;

            var deficiencies = new List<string>();
            var completedUnits = IntValue(profile, "completed_units");
            var academicDeficiencies = 0;
            foreach (var grade in grades)
            {
                var enrolled = enrolledSubjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(grade, "enrolled_subject_id"));
                var enrollment = enrolled == null ? null : enrollments.FirstOrDefault(item => IntValue(item, "id") == IntValue(enrolled, "enrollment_id"));
                if (IntValue(enrollment, "student_id") != studentId) continue;
                if (grade["is_inc"]?.GetValue<bool>() == true || (grade["final_grade"]?.GetValue<decimal>() ?? 0m) > 3m) academicDeficiencies++;
            }

            var unverifiedDocuments = documents.Count(item => IntValue(item, "student_id") == studentId && !StringValue(item, "status").Equals("Verified", StringComparison.OrdinalIgnoreCase));
            var hasNstp = nstpRecords.OfType<JsonObject>().Any(item => IntValue(item, "student_id") == studentId);
            var guidanceClearance = StringValue(profile, "guidance_clearance_status");
            if (completedUnits < 138) deficiencies.Add($"Lacks {Math.Max(0, 144 - completedUnits)} curriculum units");
            if (academicDeficiencies > 0) deficiencies.Add("Has pending INC or failing mark");
            if (unverifiedDocuments > 0) deficiencies.Add("201 credentials incomplete");
            if (!hasNstp) deficiencies.Add("Missing NSTP Serial Number");
            if (guidanceClearance != "Cleared") deficiencies.Add("Guidance clearance hold");

            var gwa = (double)(profile["average_grade"]?.GetValue<decimal>() ?? 5m);
            string? honorTitle = null;
            var honorBadge = "badge-on-leave";
            if (gwa >= 1.00 && gwa <= 1.20 && academicDeficiencies == 0)
            {
                honorTitle = "Summa Cum Laude";
                honorBadge = "badge-active";
            }
            else if (gwa > 1.20 && gwa <= 1.45 && academicDeficiencies == 0)
            {
                honorTitle = "Magna Cum Laude";
                honorBadge = "badge-open";
            }
            else if (gwa > 1.45 && gwa <= 1.75 && academicDeficiencies == 0)
            {
                honorTitle = "Cum Laude";
                honorBadge = "badge-open";
            }

            var candidate = new CandidateAuditItem
            {
                Id = studentId,
                StudentIdNumber = StringValue(user, "student_id_number"),
                Name = StringValue(user, "name"),
                Email = StringValue(user, "email"),
                Program = StringValue(profile, "program"),
                YearLevel = yearLevel,
                AverageGrade = profile["average_grade"]?.GetValue<decimal?>(),
                CompletedUnits = completedUnits,
                UnitsRemaining = IntValue(profile, "units_remaining"),
                AcademicStatus = academicStatus,
                GuidanceClearanceStatus = guidanceClearance,
                UnverifiedDocsCount = unverifiedDocuments,
                HasNstp = hasNstp ? 1 : 0,
                AcademicDeficienciesCount = academicDeficiencies,
                IsEligible = deficiencies.Count == 0,
                Deficiencies = deficiencies,
                HonorTitle = honorTitle,
                HonorBadge = honorBadge
            };
            CandidateAudit.Add(candidate);
            if (honorTitle != null && candidate.IsEligible) HonorsList.Add(candidate);
        }

        CandidateAudit = CandidateAudit.OrderBy(item => item.AverageGrade ?? 5m).ThenBy(item => item.Name).ToList();
        HonorsList = HonorsList.OrderBy(item => item.AverageGrade ?? 5m).ThenBy(item => item.Name).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostCertifyGraduateAsync(int student_id)
    {
        _db.EnsureRegistrarSession();
        var found = false;
        _store.UpdateRoot(root =>
        {
            var profile = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "user_id") == student_id);
            if (profile == null) return;
            profile["academic_status"] = "Graduated";
            profile["enrollment_status"] = "Graduated";
            found = true;
        });
        if (found)
        {
            await _db.LogActivityAsync(student_id, $"Student officially certified as Graduated by {_db.CurrentUserName}.");
            TempData["FlashMessage"] = "Candidate successfully certified as official graduate.";
            TempData["FlashType"] = "success";
        }
        else
        {
            TempData["FlashMessage"] = "Student profile was not found.";
            TempData["FlashType"] = "error";
        }
        return RedirectToPage("/GraduationAudit", new { tab = "candidates" });
    }

    private static string StringValue(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int IntValue(JsonObject? item, string property) => item?[property]?.GetValue<int>() ?? 0;
}
