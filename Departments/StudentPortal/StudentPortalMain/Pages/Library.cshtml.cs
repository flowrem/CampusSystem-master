using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class LibraryModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public LibraryModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<LibraryBookItem> Catalog { get; set; } = new();
    public List<BookLoanRecord> BookLoans { get; set; } = new();
    public decimal OutstandingFines { get; set; }
    public string SearchQuery { get; set; } = "";
    public string SelectedCategory { get; set; } = "All";

    public async Task OnGetAsync(string? search = null, string? category = null)
    {
        SearchQuery = search ?? "";
        SelectedCategory = category ?? "All";

        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        Catalog = await _db.GetLibraryCatalogAsync(SearchQuery, SelectedCategory);
        BookLoans = await _db.GetStudentBookLoansAsync(studentId);
        OutstandingFines = await _db.GetStudentOutstandingFineAmountAsync(studentId);
    }

    public async Task<IActionResult> OnPostBorrowAsync(int bookId, int loanDays = 7)
    {
        var studentId = _db.CurrentStudentId;
        try
        {
            await _db.BorrowBookAsync(studentId, bookId, loanDays);
            TempData["FlashMessage"] = "Book checkout successful! Pick up your book at the Circulation Desk (Main Stacks).";
            TempData["FlashType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["FlashMessage"] = ex.Message;
            TempData["FlashType"] = "danger";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReturnAsync(int loanId, string condition)
    {
        var studentId = _db.CurrentStudentId;
        try
        {
            await _db.ReturnBookAsync(loanId, studentId, condition);
            var outstanding = await _db.GetStudentOutstandingFineAmountAsync(studentId);
            TempData["FlashMessage"] = condition.Equals("Good", StringComparison.OrdinalIgnoreCase) && outstanding == 0
                ? "Book returned in good condition. Thank you!"
                : $"Book returned as {condition}. Fines now owed on your library account: ₱{outstanding:N2}.";
            TempData["FlashType"] = condition.Equals("Good", StringComparison.OrdinalIgnoreCase) && outstanding == 0 ? "success" : "warning";
        }
        catch (LibraryException ex)
        {
            TempData["FlashMessage"] = ex.Message;
            TempData["FlashType"] = "danger";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayFineAsync(int loanId, string paymentMethod)
    {
        var studentId = _db.CurrentStudentId;
        try
        {
            await _db.PayLibraryFineAsync(studentId, loanId, paymentMethod);
            TempData["FlashMessage"] = "Library fine settled successfully! Your library account clearance has been restored.";
            TempData["FlashType"] = "success";
        }
        catch (LibraryException ex)
        {
            TempData["FlashMessage"] = ex.Message;
            TempData["FlashType"] = "danger";
        }
        return RedirectToPage();
    }
}
