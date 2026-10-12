using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: wired to IAssessmentService, IPaymentService, IClearanceService.
    // Student-only, and always the CURRENT logged-in student's own data —
    // never accepts a studentId from the query string or form.
    [CampusRoles("student", "finance", "admin")]
    public class StudentAccountModel : PageModel
    {
        private readonly IAssessmentService _assessmentService;
        private readonly IPaymentService _paymentService;
        private readonly IClearanceService _clearanceService;
        private readonly CurrentUserContext _currentUser;

        public StudentAccountModel(
            IAssessmentService assessmentService,
            IPaymentService paymentService,
            IClearanceService clearanceService,
            CurrentUserContext currentUser)
        {
            _assessmentService = assessmentService;
            _paymentService = paymentService;
            _clearanceService = clearanceService;
            _currentUser = currentUser;
        }

        // Same term-source TODO as the other Phase 4 pages.
        public const string AcademicYear = "2026-2027";
        public const string Semester = "1st Semester";

        public string StudentName { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string Program { get; set; } = string.Empty;
        public string Term => $"{Semester} {AcademicYear}";
        public string ClearanceStatus { get; set; } = "NotCleared";
        public decimal OutstandingBalance { get; set; }
        public string AssessmentNumber { get; set; } = string.Empty;
        public bool HasAssessment { get; set; }

        public List<FeeLine> FeeBreakdown { get; set; } = new();
        public decimal TotalAmount => FeeBreakdown.Sum(f => f.Amount);
        public decimal TotalPaid => FeeBreakdown.Sum(f => f.Paid);
        public decimal TotalBalance => TotalAmount - TotalPaid;

        public List<PaymentHistoryRow> PaymentHistory { get; set; } = new();

        // Financial aid has no entity or service — Phase 1's data layer
        // never defined a FinancialAid/Scholarship model, so there is
        // nothing for Phase 4 to wire this to. Left as a UI-only mock on
        // purpose; a future phase needs to add the model + service before
        // this can become real. Do not treat this as something Phase 4
        // missed.
        public string AidName { get; set; } = "Academic Scholarship";
        public string AidDescription { get; set; } = "50% Tuition Discount";
        public decimal AidAmount { get; set; } = 12000.00m;
        public string AidStatus { get; set; } = "Active";

        public async Task OnGetAsync()
        {
            var studentId = _currentUser.GetCurrentStudentId();

            var assessment = await _assessmentService.GetCurrentForStudentAsync(studentId, AcademicYear, Semester);
            if (assessment != null)
            {
                HasAssessment = true;
                StudentName = assessment.StudentDisplayName;
                StudentId = assessment.StudentNumber;
                Program = assessment.Program;
                OutstandingBalance = assessment.Balance;
                AssessmentNumber = assessment.AssessmentNumber;
                FeeBreakdown = assessment.Items
                    .Select(i => new FeeLine(i.FeeTypeName, i.Amount, i.AmountPaid))
                    .ToList();
            }

            var clearance = await _clearanceService.GetStatusAsync(studentId, AcademicYear, Semester);
            ClearanceStatus = clearance?.Status ?? "NotCleared";

            var payments = await _paymentService.GetPaymentHistoryForStudentAsync(studentId);
            PaymentHistory = payments.Select(p => new PaymentHistoryRow(
                p.PaymentNumber, p.PaymentDate.ToString("MMM d, yyyy"), p.AmountPaid, p.PaymentMethod)).ToList();

            // Expose student identity to the _FinanceStudentLayout sidebar/header
            var displayName = string.IsNullOrWhiteSpace(StudentName) ? "Student" : StudentName;
            ViewData["StudentName"] = displayName;
            ViewData["StudentProgram"] = string.IsNullOrWhiteSpace(Program) ? "Student" : $"{Program} · Student";
            ViewData["StudentInitials"] = string.Join("", displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetter(w[0]))
                .Take(2)
                .Select(w => w[0]));
        }

        public static string ClearanceBadgeClass(string status) => status switch
        {
            "Cleared" => "badge-status badge-status-paid",
            "Conditional" => "badge-status badge-status-partial",
            _ => "badge-status badge-status-unpaid",
        };

        public static string ClearanceDisplayText(string status) => status switch
        {
            "NotCleared" => "Not Cleared",
            _ => status,
        };

        public record FeeLine(string Name, decimal Amount, decimal Paid)
        {
            public decimal Balance => Amount - Paid;
        }

        public record PaymentHistoryRow(string PaymentNumber, string Date, decimal Amount, string Method);
    }
}
