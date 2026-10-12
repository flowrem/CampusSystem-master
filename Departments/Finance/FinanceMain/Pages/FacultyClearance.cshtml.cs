using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 5, Flow 3: now wired to IClearanceService.GetForStudentsAsync,
    // fed by IClassRosterProvider (implemented by Registrar's team — see
    // that interface's doc comment). Faculty-only, still strictly
    // read-only: there is no write path into Finance data from this page,
    // matching the READ-ONLY badge.
    //
    // Until Registrar registers a real IClassRosterProvider, the
    // placeholder (Services/PendingClassRosterProvider) returns null and
    // this page falls back to the same sample roster it always showed,
    // now with a visible banner instead of silently pretending it's live.
    [CampusRoles("faculty", "finance", "admin")]
    public class FacultyClearanceModel : PageModel
    {
        private readonly IClassRosterProvider _rosterProvider;
        private readonly IClearanceService _clearanceService;

        public FacultyClearanceModel(IClassRosterProvider rosterProvider, IClearanceService clearanceService)
        {
            _rosterProvider = rosterProvider;
            _clearanceService = clearanceService;
        }

        public const string AcademicYear = "2026-2027";
        public const string Semester = "1st Semester";

        // TODO: replace with the actual logged-in faculty member's section
        // once that's resolvable from claims — same category of TODO as
        // CurrentUserContext.GetCurrentStudentId(), just not built out yet
        // because no faculty-facing claim/session lookup was in scope
        // here. Hardcoded to the one section this page has always shown.
        public string SectionId { get; set; } = "IT31S1";

        public string FacultyName { get; set; } = "";
        public string SectionLabel { get; set; } = "IT401-A · MWF 8:00–9:30";

        public bool RegistrarIntegrationAvailable { get; private set; }

        public int ClearedCount { get; set; }
        public int ConditionalCount { get; set; }
        public int NotClearedCount { get; set; }

        public List<RosterRow> Roster { get; set; } = new();

        public async Task OnGetAsync()
        {
            FacultyName = User.Identity?.Name ?? "Faculty";
            // Expose faculty identity to the _FinanceFacultyLayout
            ViewData["FacultyName"] = FacultyName;
            ViewData["FacultyInitials"] = string.Join("", FacultyName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetter(w[0]))
                .Take(2)
                .Select(w => w[0]));

            var roster = await _rosterProvider.GetRosterAsync(SectionId);

            if (roster == null)
            {
                RegistrarIntegrationAvailable = false;
                LoadSampleRoster();
                return;
            }

            RegistrarIntegrationAvailable = true;
            SectionLabel = roster.SectionLabel;

            var studentIds = roster.Students.Select(s => s.StudentId).ToList();
            var clearances = await _clearanceService.GetForStudentsAsync(studentIds, AcademicYear, Semester);
            var byStudentId = clearances.ToDictionary(c => c.StudentId, c => c.Status);

            Roster = roster.Students.Select((s, index) => new RosterRow(
                index + 1,
                s.StudentDisplayName,
                s.StudentNumber,
                DisplayStatus(byStudentId.TryGetValue(s.StudentId, out var status) ? status : "NotCleared")
            )).ToList();

            ClearedCount = Roster.Count(r => r.Status == "Cleared");
            ConditionalCount = Roster.Count(r => r.Status == "Conditional");
            NotClearedCount = Roster.Count(r => r.Status == "Not Cleared");
        }

        // A student with no FinanceClearance row yet (roster.Students has
        // them, but IClearanceService.GetForStudentsAsync found nothing)
        // reads as "Not Cleared" — the safe default, matching what
        // EvaluateAndIssueAsync would produce before any payment exists.
        private static string DisplayStatus(string status) => status switch
        {
            "NotCleared" => "Not Cleared",
            _ => status,
        };

        private void LoadSampleRoster()
        {
            // Same sample data this page has always shown pre-Phase-5 —
            // kept as the fallback so the page still demonstrates its
            // layout while Registrar's real IClassRosterProvider isn't
            // registered yet. Never presented as live data (see the
            // "Registrar integration pending" banner in the .cshtml).
            Roster = new List<RosterRow>
            {
                new(1, "Aguilar, Renzo Martin P.", "2023-00801", "Cleared"),
                new(2, "Bautista, Celine Joy A.", "2023-00815", "Cleared"),
                new(3, "Cruz, Danielle Mae S.", "2023-00822", "Conditional"),
                new(4, "Domingo, Elijah James R.", "2023-00834", "Not Cleared"),
                new(5, "Espinosa, Francine Nicole L.", "2023-00839", "Cleared"),
                new(6, "Gonzales, Ian Carlo T.", "2023-00842", "Not Cleared"),
                new(7, "Hernandez, Katrina Marie V.", "2023-00845", "Cleared"),
                new(8, "Reyes, Maria Clara D.", "2023-00847", "Not Cleared"),
            };

            ClearedCount = Roster.Count(r => r.Status == "Cleared");
            ConditionalCount = Roster.Count(r => r.Status == "Conditional");
            NotClearedCount = Roster.Count(r => r.Status == "Not Cleared");
        }

        public record RosterRow(int Index, string StudentName, string StudentId, string Status);

        public static string StatusBadgeClass(string status) => status switch
        {
            "Cleared" => "badge-status badge-status-paid",
            "Conditional" => "badge-status badge-status-partial",
            _ => "badge-status badge-status-unpaid",
        };
    }
}
