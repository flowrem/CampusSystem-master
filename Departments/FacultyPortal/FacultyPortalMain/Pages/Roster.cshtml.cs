using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class RosterModel : PageModel
{
    /// <summary>users.id of the signed-in instructor. Administrators see every section, so they get null.</summary>
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    private readonly FacultyJsonStore _db;

    public RosterModel(FacultyJsonStore db)
    {
        _db = db;
    }

    public List<FacultyJsonStore.ClassOfferingItem> Offerings { get; set; } = new();
    public List<FacultyJsonStore.StudentRosterItem> EnrolledStudents { get; set; } = new();
    public int SelectedOfferingId { get; set; }
    public FacultyJsonStore.ClassOfferingItem? CurrentOffering { get; set; }

    public void OnGet(int? sectionId = null)
    {
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
        EnrolledStudents = _db.GetClassRoster(SelectedOfferingId, InstructorId);
    }
}
