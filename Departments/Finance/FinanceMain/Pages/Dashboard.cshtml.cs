using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages;

[CampusRoles("finance", "admin")]

public class DashboardModel : PageModel
{
    private readonly IAssessmentService _assessmentService;
    private readonly IPaymentService _paymentService;
    private readonly IClearanceService _clearanceService;

    public DashboardModel(
        IAssessmentService assessmentService,
        IPaymentService paymentService,
        IClearanceService clearanceService)
    {
        _assessmentService = assessmentService;
        _paymentService = paymentService;
        _clearanceService = clearanceService;
    }

    public const string AcademicYear = "2026-2027";
    public const string Semester = "1st Semester";

    public decimal TotalAssessed { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal OutstandingReceivables { get; set; }

    public int TotalStudents { get; set; }
    public int ClearedCount { get; set; }
    public int ConditionalCount { get; set; }
    public int NotClearedCount { get; set; }
    public double ClearancePercentage { get; set; }

    public IReadOnlyList<FeeAssessmentDto> RecentAssessments { get; set; } = [];
    public IReadOnlyList<PaymentRowDto> RecentPayments { get; set; } = [];

    public async Task OnGetAsync()
    {
        var asmResult = await _assessmentService.ListAsync(new AssessmentListFilter(
            AcademicYear: AcademicYear,
            Semester: Semester,
            Status: null,
            SearchText: null,
            Page: 1,
            PageSize: 5));

        TotalAssessed = asmResult.Items.Sum(a => a.TotalAmount);
        TotalCollected = asmResult.Items.Sum(a => a.TotalPaid);
        OutstandingReceivables = asmResult.Items.Sum(a => a.Balance);
        RecentAssessments = asmResult.Items.Take(5).ToList();

        var payResult = await _paymentService.ListAsync(new PaymentListFilter(null, 1, 5));
        RecentPayments = payResult.Items.Take(5).ToList();

        var summary = await _clearanceService.GetSummaryAsync(AcademicYear, Semester);
        TotalStudents = summary.TotalStudents;
        ClearedCount = summary.Cleared;
        ConditionalCount = summary.Conditional;
        NotClearedCount = summary.NotCleared;
        ClearancePercentage = TotalStudents > 0
            ? Math.Round(((double)ClearedCount / TotalStudents) * 100, 1)
            : 0;
    }
}
