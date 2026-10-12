using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class GradebookModel : PageModel
{
    /// <summary>users.id of the signed-in instructor. Administrators see every section, so they get null.</summary>
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    private readonly FacultyJsonStore _db;

    public GradebookModel(FacultyJsonStore db)
    {
        _db = db;
    }

    public List<FacultyJsonStore.ClassOfferingItem> Offerings { get; set; } = new();
    public List<FacultyJsonStore.StudentRosterItem> Students { get; set; } = new();
    public int SelectedOfferingId { get; set; }
    public bool EncodingOpen { get; private set; }
    public FacultyJsonStore.ClassOfferingItem? CurrentOffering { get; set; }

    [TempData]
    public string? FlashMessage { get; set; }

    public void OnGet(int? sectionId = null)
    {
        EncodingOpen = _db.IsEncodingOpen();
        Offerings = _db.GetClassOfferings(InstructorId);

        if (sectionId.HasValue && sectionId.Value > 0)
        {
            SelectedOfferingId = sectionId.Value;
        }
        else if (Offerings.Any())
        {
            SelectedOfferingId = Offerings.First().Id;
        }

        CurrentOffering = Offerings.FirstOrDefault(o => o.Id == SelectedOfferingId);
        Students = _db.GetClassRoster(SelectedOfferingId, InstructorId);
    }

    public IActionResult OnPostSaveGrade(int sectionId, int enrolledSubjectId, decimal grade, string? remarks)
    {
        try
        {
            _db.SaveGrade(enrolledSubjectId, grade, remarks, InstructorId);
            FlashMessage = $"Grade of {grade:F2} saved as a draft. The Registrar verifies it before students see it.";
        }
        catch (GradeRejectedException ex)
        {
            FlashMessage = ex.Message;
        }
        return RedirectToPage(new { sectionId });
    }

    public IActionResult OnPostSaveInc(int sectionId, int enrolledSubjectId, string? incReason)
    {
        try
        {
            _db.SaveIncomplete(enrolledSubjectId, incReason, InstructorId);
            FlashMessage = "Marked Incomplete (INC) as a draft. A Guidance referral was opened for the student.";
        }
        catch (GradeRejectedException ex)
        {
            FlashMessage = ex.Message;
        }
        return RedirectToPage(new { sectionId });
    }
}
