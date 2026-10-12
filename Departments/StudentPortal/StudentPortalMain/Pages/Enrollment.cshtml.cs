using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class EnrollmentModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public EnrollmentModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public StudentEnrollmentOverview? Enrollment { get; set; }
    public List<StudentEnrollmentOverview> History { get; set; } = new();
    public List<EnrolledClassItem> AvailableOfferings { get; set; } = new();
    public List<AddDropRecord> AddDropRequests { get; set; } = new();
    public List<OverloadRecord> OverloadRequests { get; set; } = new();
    public List<CourseShiftingRecord> ShiftingRequests { get; set; } = new();
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";

    public async Task OnGetAsync(string? sy = null, string? sem = null)
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var activeTerm = await _db.GetActiveTermAsync();
        SchoolYear = !string.IsNullOrEmpty(sy) ? sy : activeTerm.SchoolYear;
        Semester = !string.IsNullOrEmpty(sem) ? sem : activeTerm.Semester;

        Enrollment = await _db.GetActiveEnrollmentOverviewAsync(studentId, SchoolYear, Semester);
        History = await _db.GetEnrollmentHistoryAsync(studentId);
        AvailableOfferings = await _db.GetAvailableOfferingsAsync(SchoolYear, Semester);

        AddDropRequests = await _db.GetAddDropRequestsAsync(studentId);
        OverloadRequests = await _db.GetOverloadRequestsAsync(studentId);
        ShiftingRequests = await _db.GetCourseShiftingRequestsAsync(studentId);
    }

    public async Task<IActionResult> OnPostAddDropAsync(int enrollmentId, string requestType, int? classOfferingId, int? targetClassOfferingId, string reason)
    {
        var studentId = _db.CurrentStudentId;
        if (enrollmentId <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            TempData["FlashMessage"] = "Please provide an enrollment reference and reason for your petition.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        await _db.SubmitAddDropRequestAsync(studentId, enrollmentId, requestType, classOfferingId, targetClassOfferingId, reason);

        TempData["FlashMessage"] = $"Your {requestType.ToUpper()} petition has been submitted to the Registrar. You can track its status below.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOverloadAsync(string schoolYear, string semester, string requestType, int requestedUnits, string reason)
    {
        var studentId = _db.CurrentStudentId;
        if (requestedUnits <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            TempData["FlashMessage"] = "Please specify valid requested units and justification.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        await _db.SubmitOverloadRequestAsync(studentId, schoolYear, semester, requestType, requestedUnits, reason);

        TempData["FlashMessage"] = $"Your {requestType.ToUpper()} waiver petition for {requestedUnits} units has been filed and sent to the Registrar.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostShiftProgramAsync(string toProgram, string reason)
    {
        var studentId = _db.CurrentStudentId;
        if (string.IsNullOrWhiteSpace(toProgram) || string.IsNullOrWhiteSpace(reason))
        {
            TempData["FlashMessage"] = "Please select target program and provide reason for shifting.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        var currentProg = Profile.Program;
        await _db.SubmitCourseShiftingRequestAsync(studentId, currentProg, toProgram.Trim(), reason.Trim());

        TempData["FlashMessage"] = $"Your Program Shifting petition to {toProgram} has been filed and queued for Registrar evaluation.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
