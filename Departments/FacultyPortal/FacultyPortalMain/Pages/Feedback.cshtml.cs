using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

/// <summary>The latest student submissions, each with a box for written feedback. Scores are given on the assignment page.</summary>
public class FeedbackModel(CourseworkStore coursework) : PageModel
{
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    public IReadOnlyList<(CourseworkStore.SubmissionRow Submission, string AssignmentTitle, string SectionCode, int AssignmentId)> Items { get; private set; } = [];

    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }

    public void OnGet() => Items = coursework.RecentSubmissions(InstructorId);

    public IActionResult OnPostSave(int submissionId, string? feedback)
    {
        try
        {
            coursework.GradeSubmission(submissionId, null, feedback, InstructorId);
            Message = "Feedback saved. The student can read it in Student Portal.";
            IsError = false;
        }
        catch (CourseworkRejectedException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
        return RedirectToPage();
    }
}
