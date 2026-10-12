using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class DashboardModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public DashboardModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public List<EnrolledClassItem> EnrolledClasses { get; set; } = new();
    public List<StudentGradeItem> RecentGrades { get; set; } = new();
    public List<DocumentRequestRecord> DocumentRequests { get; set; } = new();
    public List<AddDropRecord> AddDropRequests { get; set; } = new();
    public decimal? CumulativeGwa { get; set; }
    public int TotalEnrolledUnits { get; set; }
    public int PendingRequestsCount { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Auth guard — middleware handles the redirect, but double-check here
        var studentId = _db.CurrentStudentId;
        if (studentId <= 0)
            return RedirectToPage("/Login");

        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var term = await _db.GetActiveTermAsync();
        SchoolYear = term.SchoolYear;
        Semester = term.Semester;

        EnrolledClasses = await _db.GetEnrolledClassesAsync(studentId, SchoolYear, Semester);
        TotalEnrolledUnits = EnrolledClasses.Sum(c => c.Units);

        var gradeGroups = await _db.GetGradesGroupedByTermAsync(studentId);
        RecentGrades = gradeGroups.SelectMany(g => g.Grades).Take(5).ToList();

        CumulativeGwa = await _db.CalculateCumulativeGwaAsync(studentId) ?? Profile.AverageGrade;

        DocumentRequests = await _db.GetDocumentRequestsAsync(studentId);
        AddDropRequests = await _db.GetAddDropRequestsAsync(studentId);

        PendingRequestsCount = DocumentRequests.Count(d => d.Status == "pending" || d.Status == "processing")
                             + AddDropRequests.Count(a => a.Status == "pending");

        return Page();
    }
}
