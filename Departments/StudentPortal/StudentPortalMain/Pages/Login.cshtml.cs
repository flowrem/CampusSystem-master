using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class LoginModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public LoginModel(StudentPortalDbService db)
    {
        _db = db;
    }

    [BindProperty]
    public string Identifier { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        HttpContext.Session.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        HttpContext.Session.Clear();

        if (string.IsNullOrWhiteSpace(Identifier))
        {
            ErrorMessage = "Please enter your Student ID Number or University Email.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your password.";
            return Page();
        }

        var student = await _db.AuthenticateStudentAsync(Identifier.Trim(), Password);
        if (student == null)
        {
            ErrorMessage = _db.LastLoginError is { } why && why.StartsWith("Too many")
                ? why
                : "Invalid credentials. Check your Student ID or Email and password. Your account must be created by the Registrar's Office.";
            return Page();
        }

        // Establish session
        HttpContext.Session.SetInt32("student_user_id", student.Id);
        HttpContext.Session.SetString("student_name", student.Name);
        HttpContext.Session.SetString("student_id_number", student.StudentIdNumber);
        HttpContext.Session.SetString("student_email", student.Email);
        HttpContext.Session.SetInt32("is_authenticated", 1);

        TempData["FlashMessage"] = $"Welcome back, {student.Name}! You are now signed in.";
        TempData["FlashType"] = "success";

        return RedirectToPage("/Dashboard");
    }
}
