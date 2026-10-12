using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages;

[CampusRoles("finance", "admin")]

public class AidModel : PageModel
{
    private readonly IScholarshipService _scholarshipService;

    public AidModel(IScholarshipService scholarshipService)
    {
        _scholarshipService = scholarshipService;
    }

    public const string AcademicYear = "2026-2027";
    public const string Semester = "1st Semester";

    public IReadOnlyList<ScholarshipProgramDto> Programs { get; set; } = [];
    public IReadOnlyList<ScholarshipGranteeDto> Grantees { get; set; } = [];

    public decimal TotalScholarshipDisbursed => Grantees.Where(g => g.Status == "Active").Sum(g => g.DiscountAmount);
    public int ActiveScholarCount => Grantees.Count(g => g.Status == "Active");

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostGrantScholarshipAsync(string studentNumber, string scholarshipName, decimal discountAmount)
    {
        try
        {
            await _scholarshipService.GrantAsync(studentNumber, scholarshipName, discountAmount);
            StatusMessage = $"Institutional scholarship successfully granted to student {studentNumber.Trim()} ({discountAmount:C}).";
        }
        catch (FinanceValidationException ex)
        {
            StatusMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Programs = await _scholarshipService.GetProgramsAsync();
        Grantees = await _scholarshipService.GetGranteesAsync();
    }
}
