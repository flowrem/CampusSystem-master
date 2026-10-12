using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: rewires the original "/Invoice: invoice detail, line items,
    // totals, download placeholder" page, per the execution plan's Phase 4
    // action ("FeeAssessment + AssessmentItems — Render single assessment
    // with line items"). A Finance Officer may open any assessment via
    // ?id=; a Student is always shown their own current assessment,
    // regardless of what (if anything) is in the query string — never
    // trust the id query param for a Student caller.
    [CampusRoles("student", "finance", "admin")]
    public class InvoiceModel : PageModel
    {
        private readonly IAssessmentService _assessmentService;
        private readonly CurrentUserContext _currentUser;

        public InvoiceModel(IAssessmentService assessmentService, CurrentUserContext currentUser)
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
}
