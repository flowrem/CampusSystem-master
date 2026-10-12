using System.Globalization;
using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class AssignmentDetailModel(FacultyJsonStore faculty, CourseworkStore coursework) : PageModel
{
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    public CourseworkStore.AssignmentDetail? Detail { get; private set; }
    public List<FacultyJsonStore.ClassOfferingItem> Sections { get; private set; } = [];

    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }

    public IActionResult OnGet(int id)
    {
        Sections = faculty.GetClassOfferings(InstructorId);
        Detail = coursework.GetAssignment(id, InstructorId);
        return Detail is null ? NotFound() : Page();
    }

    public IActionResult OnPostSave(int id, int offeringId, string title, string? instructions, string type, string? dueDate, decimal maxScore, bool publish) =>
        Run(id, "Assignment saved.", () =>
        {
            var due = DateTime.TryParse(dueDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : (DateTime?)null;
            coursework.SaveAssignment(new CourseworkStore.AssignmentInput(id, offeringId, title, instructions ?? "", type, due, maxScore, publish), InstructorId);
        });

    public IActionResult OnPostPublish(int id) => Run(id, "Published. Students in the section can see it now.", () => coursework.SetStatus(id, CourseworkStore.Published, InstructorId));

    public IActionResult OnPostClose(int id) => Run(id, "Closed. Students can no longer turn work in.", () => coursework.SetStatus(id, CourseworkStore.Closed, InstructorId));

    public IActionResult OnPostReopen(int id) => Run(id, "Reopened.", () => coursework.SetStatus(id, CourseworkStore.Published, InstructorId));

    public IActionResult OnPostDelete(int id)
    {
        try
        {
            coursework.DeleteAssignment(id, InstructorId);
            Message = "Draft deleted.";
            IsError = false;
            return RedirectToPage("/Assignments");
        }
        catch (CourseworkRejectedException ex)
        {
            Message = ex.Message;
            IsError = true;
            return RedirectToPage(new { id });
        }
    }

    public IActionResult OnPostGrade(int id, int submissionId, string? score, string? feedback) =>
        Run(id, "Saved.", () =>
        {
            decimal? value = null;
            if (!string.IsNullOrWhiteSpace(score))
            {
                if (!decimal.TryParse(score, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    throw new CourseworkRejectedException("The score must be a number.");
                value = parsed;
            }
            coursework.GradeSubmission(submissionId, value, feedback, InstructorId);
        });

    private IActionResult Run(int id, string success, Action action)
    {
        try
        {
            action();
            Message = success;
            IsError = false;
        }
        catch (CourseworkRejectedException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
        return RedirectToPage(new { id });
    }
}
