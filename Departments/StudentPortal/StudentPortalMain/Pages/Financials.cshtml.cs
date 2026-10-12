using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class FinancialsModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public FinancialsModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<PaymentRecord> Payments { get; set; } = new();
    public decimal TotalAssessment { get; set; }
    public decimal LibraryFines { get; set; } = 0.00m;
    public decimal TotalPaid => Payments.Sum(p => p.Amount);
    public decimal RemainingBalance => Math.Max(0, (TotalAssessment + LibraryFines) - TotalPaid);

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        TotalAssessment = await _db.GetTotalAssessmentAsync(studentId);
        LibraryFines = await _db.GetStudentOutstandingFineAmountAsync(studentId);
        Payments = await _db.GetPaymentsAsync(studentId);
    }

    public async Task<IActionResult> OnPostPayAsync(decimal amount, string paymentMethod, string description, string category = "Tuition")
    {
        var studentId = _db.CurrentStudentId;
        if (amount <= 0 || string.IsNullOrWhiteSpace(paymentMethod))
        {
            TempData["FlashMessage"] = "Please specify a valid payment amount and payment method.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        var rcpt = await _db.SubmitPaymentAsync(studentId, amount, paymentMethod, description?.Trim() ?? "Tuition Installment Payment", category);

        TempData["FlashMessage"] = $"Payment of ₱{amount:N2} posted successfully! Official Receipt No: {rcpt}. Finance department notified.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
