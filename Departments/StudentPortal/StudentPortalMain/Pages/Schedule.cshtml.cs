using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class ScheduleModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public ScheduleModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public List<EnrolledClassItem> Classes { get; set; } = new();
    public List<string> AvailableTerms { get; set; } = new();
    public int TotalUnits => Classes.Sum(c => c.Units);
    public int TotalLecHours => Classes.Sum(c => c.LecHours);
    public int TotalLabHours => Classes.Sum(c => c.LabHours);

    public async Task OnGetAsync(string? sy = null, string? sem = null)
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var activeTerm = await _db.GetActiveTermAsync();
        SchoolYear = !string.IsNullOrEmpty(sy) ? sy : activeTerm.SchoolYear;
        Semester = !string.IsNullOrEmpty(sem) ? sem : activeTerm.Semester;

        Classes = await _db.GetEnrolledClassesAsync(studentId, SchoolYear, Semester);

        // Fetch terms student has enrollment records in
        var history = await _db.GetEnrollmentHistoryAsync(studentId);
        AvailableTerms = history.Select(h => $"{h.SchoolYear}|{h.Semester}").Distinct().ToList();
        if (!AvailableTerms.Contains($"{activeTerm.SchoolYear}|{activeTerm.Semester}"))
        {
            AvailableTerms.Insert(0, $"{activeTerm.SchoolYear}|{activeTerm.Semester}");
        }
    }
}
