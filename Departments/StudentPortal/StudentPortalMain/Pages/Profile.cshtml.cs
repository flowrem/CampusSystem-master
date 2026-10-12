using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class ProfileModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public ProfileModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public string? NstpSerial { get; set; }
    public string? NstpType { get; set; }
    public string? ChedSoNumber { get; set; }

    [BindProperty]
    public string ContactNumber { get; set; } = "";

    [BindProperty]
    public string Address { get; set; } = "";

    [BindProperty]
    public string CurrentPassword { get; set; } = "";

    [BindProperty]
    public string NewPassword { get; set; } = "";

    [BindProperty]
    public string ConfirmNewPassword { get; set; } = "";

    public bool CanChangePassword { get; private set; }

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);
        CanChangePassword = _db.CanChangeStudentPassword(studentId);

        ContactNumber = Profile.ContactNumber ?? "";
        Address = Profile.Address ?? "";

        NstpSerial = "NSTP-2023-CWTS-00918";
        NstpType = "CWTS (Civic Welfare Training Service)";
        ChedSoNumber = "CHED-RO4A-SO-2026-0812";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var studentId = _db.CurrentStudentId;
        if (string.IsNullOrWhiteSpace(ContactNumber) || string.IsNullOrWhiteSpace(Address))
        {
            TempData["FlashMessage"] = "Please provide valid contact number and address.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        await _db.UpdateContactInfoAsync(studentId, ContactNumber.Trim(), Address.Trim());

        TempData["FlashMessage"] = "Personal details updated successfully in the university database.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }

    public IActionResult OnPostChangePassword()
    {
        var studentId = _db.CurrentStudentId;
        if (studentId <= 0)
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            SetPasswordMessage("Enter your current password.", "danger");
        }
        else if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 12)
        {
            SetPasswordMessage("Your new password must be at least 12 characters.", "danger");
        }
        else if (Encoding.UTF8.GetByteCount(NewPassword) > 72)
        {
            SetPasswordMessage("Your new password must be no more than 72 UTF-8 bytes.", "danger");
        }
        else if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            SetPasswordMessage("The new password and confirmation do not match.", "danger");
        }
        else
        {
            var result = _db.ChangeStudentPassword(studentId, CurrentPassword, NewPassword);
            switch (result)
            {
                case PasswordChangeResult.Changed:
                    SetPasswordMessage("Your password has been updated.", "success");
                    break;
                case PasswordChangeResult.Ineligible:
                    SetPasswordMessage("Password changes are available after Registrar activates your account or confirms your enrollment.", "danger");
                    break;
                case PasswordChangeResult.InvalidCurrentPassword:
                    SetPasswordMessage("Your current password is incorrect.", "danger");
                    break;
                default:
                    SetPasswordMessage("Your password could not be updated. Please try again.", "danger");
                    break;
            }
        }

        return RedirectToPage();
    }

    private void SetPasswordMessage(string message, string type)
    {
        TempData["FlashMessage"] = message;
        TempData["FlashType"] = type;
    }
}
