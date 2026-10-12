using CampusSystem.Data.Auth;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;
using FinanceMain.Security;
using FinanceMain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinanceMain.Pages
{
    // Phase 4: wired to IRequestService. Student-only, and always the
    // CURRENT logged-in student — the "Submit Request & Pay" button now
    // does two real service calls (CreateRequestAsync then
    // PayRequestFeeAsync) in one step, matching how the UI presents it as
    // a single action.
    [CampusRoles("student", "finance", "admin")]
    public class RequestFeesModel : PageModel
    {
        private readonly IRequestService _requestService;
        private readonly CurrentUserContext _currentUser;

        public RequestFeesModel(IRequestService requestService, CurrentUserContext currentUser)
        {
            _requestService = requestService;
            _currentUser = currentUser;
        }

        public List<DocumentFeeTypeDto> DocumentTypes { get; set; } = new();
        public List<RequestRow> Requests { get; set; } = new();

        // Base processing fee is added by the service automatically; kept
        // here only for the fee-summary preview before the form is submitted.
        public const decimal BaseProcessingFee = FinanceJsonStore.BaseProcessingFee;

        [BindProperty]
        public int SelectedDocumentFeeTypeId { get; set; }

        [BindProperty]
        public int Copies { get; set; } = 1;

        [BindProperty]
        public string? Purpose { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var studentId = _currentUser.GetCurrentStudentId();
            var docType = (await _requestService.GetDocumentFeeTypesAsync())
                .FirstOrDefault(d => d.Id == SelectedDocumentFeeTypeId);

            if (docType == null)
            {
                ErrorMessage = "Select a document type before submitting.";
                await LoadAsync();
                return Page();
            }

            try
            {
                var created = await _requestService.CreateRequestAsync(new CreateRequestRequest(
                    StudentId: studentId,
                    DocumentType: docType.Name,
                    DocumentFeeTypeId: docType.Id,
                    Copies: Copies,
                    Purpose: Purpose));

                // The UI presents "Submit Request & Pay" as one action —
                // Phase 4 treats that as create-then-immediately-pay rather
                // than a separate payment step, since /RequestFees has no
                // "pending payment" queue view of its own.
                await _requestService.PayRequestFeeAsync(created.Id, new PayRequestFeeRequest(
                    PaymentMethod: "Over-the-counter"));

                StatusMessage = $"Request {created.RequestNumber} submitted and paid.";
                return RedirectToPage();
            }
            catch (FinanceValidationException ex)
            {
                ErrorMessage = ex.Message;
                await LoadAsync();
                return Page();
            }
        }

        private async Task LoadAsync()
        {
            var studentId = _currentUser.GetCurrentStudentId();

            DocumentTypes = (await _requestService.GetDocumentFeeTypesAsync()).ToList();
            if (SelectedDocumentFeeTypeId == 0 && DocumentTypes.Count > 0)
                SelectedDocumentFeeTypeId = DocumentTypes[0].Id;

            var requests = await _requestService.GetRequestsForStudentAsync(studentId);
            Requests = requests.Select(r => new RequestRow(
                r.RequestNumber, r.DocumentType, r.ProcessingFee,
                r.Payments.Any(p => p.Status == "Paid") ? "Paid" : "Pending",
                r.Status)).ToList();
        }

        public decimal SelectedDocumentFee =>
            DocumentTypes.FirstOrDefault(d => d.Id == SelectedDocumentFeeTypeId)?.Amount ?? 0m;

        public decimal TotalFeePreview => (SelectedDocumentFee * Math.Max(1, Copies)) + BaseProcessingFee;

        public record RequestRow(
            string RequestNumber,
            string Document,
            decimal Fee,
            string PaymentStatus,
            string RequestStatus);

        public static string PaymentBadgeClass(string status) => status switch
        {
            "Paid" => "badge-status badge-status-paid",
            "Pending" => "badge-status badge-status-partial",
            _ => "badge-status badge-status-unpaid",
        };

        public static string RequestBadgeClass(string status) => status switch
        {
            "Released" => "badge-status badge-status-released",
            _ => "badge-status badge-status-processing",
        };
    }
}
