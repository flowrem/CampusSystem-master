using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages;

[CampusRoles("student", "finance", "admin")]

public class OAOModel : PageModel
{
    private readonly IAssessmentService _assessmentService;
    private readonly CurrentUserContext _currentUser;

    public OAOModel(IAssessmentService assessmentService, CurrentUserContext currentUser)
    {
        _assessmentService = assessmentService;
        _currentUser = currentUser;
    }

    public const string AcademicYear = "2026-2027";
    public const string Semester = "1st Semester";

    public FeeAssessmentDto? Assessment { get; private set; }
    new public bool NotFound { get; private set; }

    public async Task OnGetAsync(int? id)
    {
        if (id.HasValue && id.Value > 0)
        {
            Assessment = await _assessmentService.GetByIdAsync(id.Value);
        }
        else
        {
            var studentId = _currentUser.GetCurrentStudentId();
            Assessment = await _assessmentService.GetCurrentForStudentAsync(studentId, AcademicYear, Semester);
        }

        if (Assessment != null && !_currentUser.CanView(Assessment.StudentNumber)) Assessment = null;
        NotFound = Assessment == null;
    }
}
