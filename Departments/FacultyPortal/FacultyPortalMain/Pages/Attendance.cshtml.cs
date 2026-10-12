using System.Globalization;
using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class AttendanceModel(FacultyJsonStore faculty, CourseworkStore coursework) : PageModel
{
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    public List<FacultyJsonStore.ClassOfferingItem> Sections { get; private set; } = [];
    public int SectionId { get; private set; }
    public DateOnly Date { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    public CourseworkStore.AttendanceSheet Sheet { get; private set; } = new("", []);
    public IReadOnlyList<CourseworkStore.AttendanceSummaryRow> Summary { get; private set; } = [];

    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }

    public void OnGet(int? sectionId, string? date) => Load(sectionId, date);

    public IActionResult OnPostSave(int sectionId, string date, string? topic)
    {
        var statuses = Request.Form
            .Where(pair => pair.Key.StartsWith("status_", StringComparison.Ordinal) && int.TryParse(pair.Key[7..], out _))
            .ToDictionary(pair => int.Parse(pair.Key[7..], CultureInfo.InvariantCulture), pair => pair.Value.ToString());

        try
        {
            if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var day)) throw new CourseworkRejectedException("Choose a valid date.");
            var saved = coursework.SaveAttendance(sectionId, day, topic, statuses, InstructorId);
            Message = $"Attendance saved for {saved} student(s) on {day:MMM d, yyyy}.";
            IsError = false;
        }
        catch (CourseworkRejectedException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
        return RedirectToPage(new { sectionId, date });
    }

    private void Load(int? sectionId, string? date)
    {
        Sections = faculty.GetClassOfferings(InstructorId);
        SectionId = sectionId is { } id && Sections.Any(s => s.Id == id) ? id : Sections.FirstOrDefault()?.Id ?? 0;
        if (DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var parsed)) Date = parsed;
        if (SectionId == 0) return;

        Sheet = coursework.GetAttendanceSheet(SectionId, Date, InstructorId);
        Summary = coursework.GetAttendanceSummary(SectionId, InstructorId);
    }
}
