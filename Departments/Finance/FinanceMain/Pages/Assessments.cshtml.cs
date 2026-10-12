using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: wired to IAssessmentService. Finance-officer-only — students
    // see their own assessment via /StudentAccount, not this admin list.
    [CampusRoles("finance", "admin")]
    public class AssessmentsModel : PageModel
    {
        private readonly IAssessmentService _assessmentService;

        public AssessmentsModel(IAssessmentService assessmentService)
        {
            _assessmentService = assessmentService;
        }

        public List<AssessmentRow> Assessments { get; set; } = new();
        public int TotalCount { get; set; }

        // TODO: replace with the actual current AY/Semester source once
        // one exists (a shared academic-calendar service, or the
        // term <select> on this page wired to a query-string parameter).
        public const string AcademicYear = "2026-2027";
        public const string Semester = "1st Semester";

        public async Task OnGetAsync(string? status = null, string? search = null, int page = 1)
        {
            var result = await _assessmentService.ListAsync(new AssessmentListFilter(
                AcademicYear: AcademicYear,
                Semester: Semester,
                Status: status,
                SearchText: search,
                Page: page,
                PageSize: 20));

            TotalCount = result.TotalCount;
            Assessments = result.Items.Select(a => new AssessmentRow(
                a.Id, a.AssessmentNumber, a.StudentDisplayName, a.Program, a.TotalAmount, a.Balance, a.Status)).ToList();
        }

        public record AssessmentRow(
            int Id,
            string AssessmentNumber,
            string StudentName,
            string Program,
            decimal Total,
            decimal Balance,
            string Status);

        public static string StatusBadgeClass(string status) => status switch
        {
            "Paid" => "badge-status badge-status-paid",
            "Partial" => "badge-status badge-status-partial",
            _ => "badge-status badge-status-unpaid",
        };
    }
}
