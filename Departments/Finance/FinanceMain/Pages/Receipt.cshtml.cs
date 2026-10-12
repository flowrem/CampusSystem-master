using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages;

[CampusRoles("student", "finance", "admin")]

public class ReceiptModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IAssessmentService _assessmentService;
    private readonly CurrentUserContext _currentUser;

    public ReceiptModel(
        IPaymentService paymentService,
        IAssessmentService assessmentService,
        CurrentUserContext currentUser)
    {
        _paymentService = paymentService;
        _assessmentService = assessmentService;
        _currentUser = currentUser;
    }

    public PaymentRowDto? Payment { get; private set; }
    public FeeAssessmentDto? Assessment { get; private set; }
    new public bool NotFound { get; private set; }

    public async Task OnGetAsync(string? paymentNumber)
    {
        if (!string.IsNullOrWhiteSpace(paymentNumber))
        {
            Payment = await _paymentService.GetPaymentByNumberAsync(paymentNumber.Trim());
        }
        else
        {
            var studentId = _currentUser.GetCurrentStudentId();
            var history = await _paymentService.GetPaymentHistoryForStudentAsync(studentId);
            var latest = history.FirstOrDefault();
            if (latest != null)
            {
                Payment = await _paymentService.GetPaymentByNumberAsync(latest.PaymentNumber);
            }
        }

        if (Payment != null)
        {
            Assessment = await _assessmentService.GetByIdAsync(Payment.FeeAssessmentId);
        }

        // A student may only open their own receipt. A payment with no assessment cannot be tied to anyone, so only staff see it.
        if (Payment != null && !(Assessment is not null ? _currentUser.CanView(Assessment.StudentNumber) : !_currentUser.IsStudent))
        {
            Payment = null;
            Assessment = null;
        }

        NotFound = Payment == null;
    }
}
