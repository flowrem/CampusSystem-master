using CampusSystem.Data.Auth;
using FacultyPortalMain.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FacultyPortalMain.Pages;

public class ScheduleModel : PageModel
{
    /// <summary>users.id of the signed-in instructor. Administrators see every section, so they get null.</summary>
    private int? InstructorId => User.GetRole() == "faculty" ? User.GetUserId() : null;

    private readonly FacultyJsonStore _db;

    public ScheduleModel(FacultyJsonStore db)
    {
        _db = db;
    }

    public List<FacultyJsonStore.ClassOfferingItem> Classes { get; set; } = new();

    public void OnGet()
    {
        Classes = _db.GetClassOfferings(InstructorId);
    }
}
