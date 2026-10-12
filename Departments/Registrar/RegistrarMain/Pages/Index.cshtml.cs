using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RegistrarMain.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/Dashboard");
    }
}
