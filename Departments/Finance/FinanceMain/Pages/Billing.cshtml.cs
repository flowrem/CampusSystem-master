using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages;

[CampusRoles("finance", "admin")]

public class BillingModel : PageModel
{
    private readonly IAssessmentService _assessmentService;

    public BillingModel(IAssessmentService assessmentService)
    {
        _assessmentService = assessmentService;
    }

    public const string AcademicYear = "2026-2027";
    public const string Semester = "1st Semester";

    public List<BillingStatementRow> Statements { get; set; } = [];
    public decimal TotalBilled { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalOutstanding { get; set; }
    public int TotalAccounts { get; set; }

    public async Task OnGetAsync(string? search = null, string? status = null)
    {
        var result = await _assessmentService.ListAsync(new AssessmentListFilter(
            AcademicYear: AcademicYear,
            Semester: Semester,
            Status: status,
            SearchText: search,
            Page: 1,
            PageSize: 50));

        TotalAccounts = result.TotalCount;
        TotalBilled = result.Items.Sum(a => a.TotalAmount);
        TotalCollected = result.Items.Sum(a => a.TotalPaid);
        TotalOutstanding = result.Items.Sum(a => a.Balance);

        Statements = result.Items.Select(a => new BillingStatementRow(
            a.Id,
            a.AssessmentNumber,
            a.StudentDisplayName,
            a.StudentNumber,
            a.Program,
            a.TotalAmount,
            a.TotalPaid,
            a.Balance,
            a.Status,
            a.AssessedDate
        )).ToList();
    }

    public record BillingStatementRow(
        int Id,
        string AssessmentNumber,
        string StudentName,
        string StudentNumber,
        string Program,
        decimal TotalBilled,
        decimal TotalPaid,
        decimal Balance,
        string Status,
        DateTime DateBilled);

    public static string StatusBadge(string status) => status switch
    {
        "Paid" => "badge-status badge-status-paid",
        "Partial" => "badge-status badge-status-partial",
        _ => "badge-status badge-status-unpaid"
    };
}
