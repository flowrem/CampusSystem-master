using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: rewires the original "/Payments: payment history table"
    // placeholder into the admin-wide payment log (every student, most
    // recent first), backed by the IPaymentService.ListAsync method added
    // in Phase 4. A student's OWN payment history lives on /StudentAccount
    // instead — this page is Finance-officer-only.
    [CampusRoles("finance", "admin")]
    public class PaymentsModel : PageModel
    {
        private readonly IPaymentService _paymentService;

        public PaymentsModel(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        public List<PaymentRowDto> Payments { get; set; } = new();
        public int TotalCount { get; set; }

        public async Task OnGetAsync(string? search = null, int page = 1)
        {
            var result = await _paymentService.ListAsync(new PaymentListFilter(search, page, 20));
            Payments = result.Items.ToList();
            TotalCount = result.TotalCount;
        }
    }
}
