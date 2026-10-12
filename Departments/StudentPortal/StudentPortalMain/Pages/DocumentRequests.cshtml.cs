using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class DocumentRequestsModel : PageModel
{
    private readonly StudentPortalDbService _db;

    public DocumentRequestsModel(StudentPortalDbService db)
    {
        _db = db;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();
    public List<DocumentRequestRecord> Requests { get; set; } = new();
    public List<DocumentCredentialItem> Vault { get; set; } = new();

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        Requests = await _db.GetDocumentRequestsAsync(studentId);
        Vault = await _db.GetDocumentVaultAsync(studentId);
    }

    public async Task<IActionResult> OnPostRequestDocumentAsync(string documentType, string purpose, int copies)
    {
        var studentId = _db.CurrentStudentId;
        if (string.IsNullOrWhiteSpace(documentType))
        {
            TempData["FlashMessage"] = "Please select a document type to request.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        var token = await _db.SubmitDocumentRequestAsync(studentId, documentType.Trim(), purpose?.Trim() ?? "Official requirement", Math.Max(1, copies));

        TempData["FlashMessage"] = $"Document request submitted successfully! Your tracking token is {token}. Registrar has been notified.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSubmitVaultDocAsync(int credentialId, string documentName)
    {
        var studentId = _db.CurrentStudentId;
        if (credentialId <= 0)
        {
            return RedirectToPage();
        }

        var fileName = $"{documentName.Replace(" ", "_").ToLower()}_{DateTime.UtcNow.Ticks}.pdf";
        await _db.SubmitVaultFileAsync(credentialId, studentId, fileName);

        TempData["FlashMessage"] = $"{documentName} marked as submitted and forwarded to Registrar for verification.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
