using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StudentPortalMain.Pages;

public class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        HttpContext.Session.Clear();
        TempData["FlashMessage"] = "You have signed out successfully.";
        TempData["FlashType"] = "info";
        return RedirectToPage("/Login");
    }

    public IActionResult OnPost()
    {
        HttpContext.Session.Clear();
        TempData["FlashMessage"] = "You have signed out successfully.";
        TempData["FlashType"] = "info";
        return RedirectToPage("/Login");
    }
}
