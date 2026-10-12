using LibraryMain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryMain.Pages;

public class ReservationsModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public IReadOnlyList<LibraryDesk.ReservationRow> Reservations { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public void OnGet() => Reservations = Desk.Reservations(OwnerFilter, Status);

    public IActionResult OnPostCancel(int reservationId)
    {
        Run(() => Desk.CancelReservation(reservationId, OwnerFilter, UserId), "Reservation cancelled.");
        return RedirectToPage(new { Status });
    }
}
