using CampusSystem.Data.Json;
using LibraryMain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryMain.Pages;

public class HistoryModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public IReadOnlyList<LibraryDesk.LoanRow> Loans { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public void OnGet() => Loans = Desk.Loans(OwnerFilter, Filter, Q);

    public IActionResult OnPostCheckIn(int loanId, string condition)
    {
        if (!IsStaff) { Fail("Only library staff can check books in."); return RedirectToPage(); }
        try
        {
            var result = Desk.CheckIn(loanId, condition, UserId);
            Message = result.TotalFine > 0
                ? $"Checked in. Fines assessed: ₱{result.TotalFine:N2} ({string.Join(", ", result.Reasons)})."
                : "Checked in. No fine.";
            IsError = false;
        }
        catch (LibraryException ex)
        {
            Fail(ex.Message);
        }
        return RedirectToPage(new { Filter, Q });
    }
}
