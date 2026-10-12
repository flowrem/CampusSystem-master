using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class ClearanceModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public ClearanceModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<ClearanceRecord> ClearanceList { get; set; } = new();
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public bool IsAllCleared => ClearanceList.Any() && ClearanceList.All(c => c.Status == "Cleared");

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var term = await _db.GetActiveTermAsync();
        SchoolYear = term.SchoolYear;
        Semester = term.Semester;

        ClearanceList = await _db.GetClearanceRecordsAsync(studentId, SchoolYear, Semester);
    }
}
