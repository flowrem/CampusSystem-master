using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: wired to IClearanceService. Finance-officer-only.
    [CampusRoles("finance", "admin")]
    public class ClearanceModel : PageModel
    {
        private readonly IClearanceService _clearanceService;

        public ClearanceModel(IClearanceService clearanceService)
        {
            _clearanceService = clearanceService;
        }

        public int TotalStudents { get; set; }
        public int Cleared { get; set; }
        public int Conditional { get; set; }
        public int NotCleared { get; set; }
        public int TotalCount { get; set; }

        public List<ClearanceRow> Rows { get; set; } = new();

        // Same term-source TODO as Assessments.cshtml.cs.
        public const string AcademicYear = "2026-2027";
        public const string Semester = "1st Semester";

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(string? status = null, string? search = null, int page = 1)
        {
            var summary = await _clearanceService.GetSummaryAsync(AcademicYear, Semester);
            TotalStudents = summary.TotalStudents;
            Cleared = summary.Cleared;
            Conditional = summary.Conditional;
            NotCleared = summary.NotCleared;

            var result = await _clearanceService.ListAsync(new ClearanceListFilter(
                AcademicYear, Semester, status, search, page, 20));

            TotalCount = result.TotalCount;
            Rows = result.Items.Select(r => new ClearanceRow(
                r.Id, r.StudentDisplayName, r.Program, r.Assessed ?? 0m, r.Balance ?? 0m, r.Status,
                r.IssuedDate?.ToString("MMM d, yyyy"))).ToList();
        }

        // Wires the "Review" action — the Finance officer's manual path to
        // Conditional, per IClearanceService.SetConditionalAsync. The
        // "Batch Issue" and "Export" buttons stay disabled placeholders:
        // neither has a backing service method yet (batch re-evaluation
        // and CSV/PDF export are not in the Phase 2 contract) — a future
        // phase, not an oversight here.
        public async Task<IActionResult> OnPostReviewAsync(int clearanceId, string remarks)
        {
            try
            {
                await _clearanceService.SetConditionalAsync(clearanceId, new SetConditionalRequest(remarks));
                StatusMessage = "Clearance marked Conditional.";
            }
            catch (FinanceValidationException ex)
            {
                StatusMessage = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostBatchIssueAsync()
        {
            var cleared = await _clearanceService.BatchIssueAsync(AcademicYear, Semester);
            StatusMessage = $"Batch evaluation completed: {cleared} student(s) with zero balance officially marked Cleared.";
            return RedirectToPage();
        }

        public record ClearanceRow(
            int Id,
            string StudentName,
            string Program,
            decimal Assessed,
            decimal Balance,
            string Status,
            string? ClearedOn);

        public static string StatusBadgeClass(string status) => status switch
        {
            "Cleared" => "badge-status badge-status-paid",
            "Conditional" => "badge-status badge-status-partial",
            _ => "badge-status badge-status-unpaid",
        };
    }
}
