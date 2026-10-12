using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class GradesModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public GradesModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<TermGradeGroup> TermGrades { get; set; } = new();
    public decimal? CumulativeGwa { get; set; }
    public int TotalUnitsEarned { get; set; }
    public List<CompletionRevisionRecord> MyRevisionRequests { get; set; } = new();

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        TermGrades = await _db.GetGradesGroupedByTermAsync(studentId);
        CumulativeGwa = await _db.CalculateCumulativeGwaAsync(studentId) ?? Profile.AverageGrade;

        TotalUnitsEarned = TermGrades
            .SelectMany(t => t.Grades)
            .Where(g => g.Grade.HasValue && g.Grade.Value <= 3.00m && !g.IsInc)
            .Sum(g => g.Units);

        MyRevisionRequests = await _db.GetCompletionRevisionRequestsAsync(studentId);
    }

    public async Task<IActionResult> OnPostRequestRevisionAsync(int gradeId, string requestType, decimal requestedGrade, string reason)
    {
        var studentId = _db.CurrentStudentId;
        if (gradeId <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            TempData["FlashMessage"] = "Please provide complete information for your grade request.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        await _db.SubmitCompletionRevisionAsync(studentId, gradeId, "IT311", requestType, requestedGrade, reason);

        TempData["FlashMessage"] = $"Your {requestType} request has been submitted to the Registrar for review.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
