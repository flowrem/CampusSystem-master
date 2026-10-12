using LibraryMain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryMain.Pages;

public class CatalogModel(LibraryDesk desk) : LibraryPageModel(desk)
{
    public IReadOnlyList<LibraryDesk.BookRow> Books { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public bool AvailableOnly { get; set; }

    public void OnGet()
    {
        Categories = Desk.Categories();
        Books = Desk.Catalog(Q, Category, AvailableOnly);
    }
}
