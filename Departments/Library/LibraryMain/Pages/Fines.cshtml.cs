using CampusSystem.Data.Json;
using LibraryMain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryMain.Pages;

public class FinesModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public IReadOnlyList<LibraryDesk.FineRow> Fines { get; private set; } = [];
    public decimal Outstanding { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public void OnGet()
    {
        Fines = Desk.Fines(OwnerFilter, Status);
        if (IsStudent) Outstanding = Desk.Outstanding(UserId);
    }

    public IActionResult OnPostPay(int fineId, string method)
    {
        if (!IsStaff) { Fail("Fines are paid at the library desk or in Student Portal."); return RedirectToPage(); }
        try
        {
            var receipt = Desk.RecordFinePayment(fineId, method, UserId);
            Message = $"Payment recorded. Receipt {receipt}.";
            IsError = false;
        }
        catch (LibraryException ex)
        {
            Fail(ex.Message);
        }
        return RedirectToPage(new { Status });
    }

    public IActionResult OnPostWaive(int fineId, string reason)
    {
        if (!IsStaff) { Fail("Only library staff can waive a fine."); return RedirectToPage(); }
        Run(() => Desk.WaiveFine(fineId, reason, UserId), "Fine waived.");
        return RedirectToPage(new { Status });
    }
}
