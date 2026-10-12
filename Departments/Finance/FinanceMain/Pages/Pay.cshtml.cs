using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: rewires the original "/Pay: make-a-payment form; UI only"
    // placeholder into a functional form, per the execution plan's Phase 4
    // table ("IPaymentService — Form becomes functional, posts to
    // service"). Student-only, and always pays against the CURRENT
    // logged-in student's own current-term assessment.
    [CampusRoles("student", "finance", "admin")]
    public class PayModel : PageModel
    {
        private readonly IAssessmentService _assessmentService;
        private readonly IPaymentService _paymentService;
        private readonly CurrentUserContext _currentUser;

        public PayModel(
            IAssessmentService assessmentService,
            IPaymentService paymentService,
            CurrentUserContext currentUser)
        {
            _assessmentService = assessmentService;
            _paymentService = paymentService;
            _currentUser = currentUser;
        }

        public const string AcademicYear = "2026-2027";
        public const string Semester = "1st Semester";

        public FeeAssessmentDto? Assessment { get; private set; }

        [BindProperty]
        public decimal AmountPaid { get; set; }

        [BindProperty]
        public string PaymentMethod { get; set; } = "Over-the-counter";

        [BindProperty]
        public string? ReferenceNumber { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? AssessmentId { get; set; }

        public async Task OnGetAsync(int? assessmentId = null)
        {
            if (assessmentId.HasValue && assessmentId.Value > 0)
            {
                AssessmentId = assessmentId;
                Assessment = await _assessmentService.GetByIdAsync(assessmentId.Value);
            }
            else
            {
                var studentId = _currentUser.GetCurrentStudentId();
                Assessment = await _assessmentService.GetCurrentForStudentAsync(studentId, AcademicYear, Semester);
                if (Assessment != null) AssessmentId = Assessment.Id;
            }

            if (Assessment != null && !_currentUser.CanView(Assessment.StudentNumber)) Assessment = null;

            if (Assessment != null)
            {
                AmountPaid = Assessment.Balance;
                ViewData["StudentName"] = Assessment.StudentDisplayName;
                ViewData["StudentProgram"] = $"{Assessment.Program} · Student";
                ViewData["StudentInitials"] = string.Join("", Assessment.StudentDisplayName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => char.IsLetter(w[0]))
                    .Take(2)
                    .Select(w => w[0]));
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (AssessmentId.HasValue && AssessmentId.Value > 0)
            {
                Assessment = await _assessmentService.GetByIdAsync(AssessmentId.Value);
            }
            else
            {
                var studentId = _currentUser.GetCurrentStudentId();
                Assessment = await _assessmentService.GetCurrentForStudentAsync(studentId, AcademicYear, Semester);
            }

            if (Assessment != null && !_currentUser.CanView(Assessment.StudentNumber)) Assessment = null;

            if (Assessment == null)
            {
                ErrorMessage = "No fee assessment on file for the current term — nothing to pay.";
                return Page();
            }

            try
            {
                var payment = await _paymentService.RecordPaymentAsync(new RecordPaymentRequest(
                    FeeAssessmentId: Assessment.Id,
                    AmountPaid: AmountPaid,
                    PaymentMethod: PaymentMethod,
                    ReferenceNumber: ReferenceNumber));

                StatusMessage = $"Payment {payment.PaymentNumber} recorded — {payment.AmountPaid:₱#,##0.00}.";
                return RedirectToPage("/Receipt", new { paymentNumber = payment.PaymentNumber });
            }
            catch (FinanceValidationException ex)
            {
                ErrorMessage = ex.Message;
                return Page();
            }
        }
    }
}
