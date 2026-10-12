using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class ScholarshipsModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public ScholarshipsModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<ScholarshipItem> AvailableScholarships { get; set; } = new();
    public List<ScholarshipApplicationRecord> Applications { get; set; } = new();
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var term = await _db.GetActiveTermAsync();
        SchoolYear = term.SchoolYear;
        Semester = term.Semester;

        AvailableScholarships = _db.GetAvailableScholarships();
        Applications = await _db.GetScholarshipApplicationsAsync(studentId);
    }

    public async Task<IActionResult> OnPostApplyAsync(string scholarshipName)
    {
        var studentId = _db.CurrentStudentId;
        if (string.IsNullOrWhiteSpace(scholarshipName))
        {
            TempData["FlashMessage"] = "Please select a scholarship grant.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        var term = await _db.GetActiveTermAsync();
        await _db.SubmitScholarshipApplicationAsync(studentId, scholarshipName.Trim(), term.SchoolYear, term.Semester);

        TempData["FlashMessage"] = $"Your application for '{scholarshipName}' has been received and queued for review.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
