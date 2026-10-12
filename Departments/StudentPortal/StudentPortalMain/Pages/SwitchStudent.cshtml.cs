using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

// Account switching has been removed.
// Students must Sign Out and Sign In with their own credentials.
// This page now simply redirects back to Dashboard.
public class SwitchStudentModel : PageModel
{
    public IActionResult OnGet(int id = 0, string returnUrl = "/Dashboard")
    {
        // Account switching is disabled — students can only use their own account.
        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Dashboard");
    }
}
