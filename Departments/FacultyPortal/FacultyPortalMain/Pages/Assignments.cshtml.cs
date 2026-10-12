using System.Globalization;
using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class AssignmentsModel(FacultyJsonStore faculty, CourseworkStore coursework) : PageModel
{
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    public List<FacultyJsonStore.ClassOfferingItem> Sections { get; private set; } = [];
    public IReadOnlyList<CourseworkStore.AssignmentRow> Assignments { get; private set; } = [];

    public int? SectionFilter { get; private set; }
    public string? StatusFilter { get; private set; }
    public string? Search { get; private set; }

    public int PublishedCount => Assignments.Count(a => a.Status == CourseworkStore.Published);
    public int ToGradeCount => Assignments.Sum(a => a.Submitted - a.Graded);
    public int DraftCount => Assignments.Count(a => a.Status == CourseworkStore.Draft);
    public int DueThisWeek => Assignments.Count(a => a.Status == CourseworkStore.Published && a.DueDate is { } due && due >= DateTime.UtcNow && due <= DateTime.UtcNow.AddDays(7));

    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }

    public void OnGet(int? sectionId, string? status, string? q)
    {
        Sections = faculty.GetClassOfferings(InstructorId);
        SectionFilter = sectionId;
        StatusFilter = status;
        Search = q;
        Assignments = coursework.ListAssignments(InstructorId, sectionId, status, q);
    }

    public IActionResult OnPostCreate(int offeringId, string title, string? instructions, string type, string? dueDate, decimal maxScore, bool publish)
    {
        try
        {
            var due = DateTime.TryParse(dueDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : (DateTime?)null;
            var id = coursework.SaveAssignment(new CourseworkStore.AssignmentInput(null, offeringId, title, instructions ?? "", type, due, maxScore, publish), InstructorId);
            Message = publish ? "Assignment published to the section's students." : "Assignment saved as a draft.";
            IsError = false;
            return RedirectToPage("/AssignmentDetail", new { id });
        }
        catch (CourseworkRejectedException ex)
        {
            Message = ex.Message;
            IsError = true;
            return RedirectToPage();
        }
    }

    public static string BadgeClass(string status) => status switch
    {
        CourseworkStore.Published => "badge-status badge-success",
        CourseworkStore.Closed => "badge-status badge-secondary",
        _ => "badge-status badge-warning",
    };
}
