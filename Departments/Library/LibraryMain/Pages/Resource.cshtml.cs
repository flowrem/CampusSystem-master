using LibraryMain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryMain.Pages;

public class ResourceModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public LibraryDesk.BookRow? Book { get; private set; }

    public IActionResult OnGet(int id)
    {
        Book = Desk.Book(id);
        return Book is null ? NotFound() : Page();
    }

    public IActionResult OnPostReserve(int id)
    {
        if (!IsStudent) { Fail("Only students can reserve a book."); return RedirectToPage(new { id }); }
        Run(() => Desk.Reserve(UserId, id), "Reserved. You will see it under Reservations for 7 days.");
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostCheckout(int id, string studentNumber, int? loanDays)
    {
        if (!IsStaff) { Fail("Only library staff can check books out."); return RedirectToPage(new { id }); }
        Run(() => Desk.Checkout(studentNumber, id, loanDays, UserId), "Checked out.");
        return RedirectToPage(new { id });
    }
}
