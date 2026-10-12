using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using StudentPortalMain.Models;
using StudentPortalMain.Services;

namespace StudentPortalMain.Pages;

public class RequestsModel : PageModel
{
    private readonly StudentPortalDbService _db;
    private readonly CanonicalJsonStore _jsonStore;

    public RequestsModel(StudentPortalDbService db, CanonicalJsonStore jsonStore)
    {
        _db = db;
        _jsonStore = jsonStore;
    }

    public StudentUser? Student { get; set; }
    public StudentProfileData Profile { get; set; } = new();

    public record GuidanceTicket(string Title, string Category, string Details, string Urgency, string Status, DateTime CreatedAt);
    public List<GuidanceTicket> MyTickets { get; set; } = new();

    public async Task OnGetAsync()
    {
        var studentId = _db.CurrentStudentId;
        Student = await _db.GetStudentUserAsync(studentId);
        Profile = await _db.GetStudentProfileAsync(studentId);

        var root = _jsonStore.ReadRoot();
        // Guidance Department's triage service reads and updates these same rows.
        var tickets = (root["unified_seed_data"]?["guidance_requests"] as JsonArray ?? new JsonArray());
        foreach (var ticket in tickets.OfType<JsonObject>())
        {
            if (ticket["student_id"]?.GetValue<int>() != studentId) continue;
            MyTickets.Add(new GuidanceTicket(
                ticket["subject"]?.GetValue<string>() ?? ticket["title"]?.GetValue<string>() ?? "Guidance Request",
                ticket["category"]?.GetValue<string>() ?? "Academic",
                ticket["details"]?.GetValue<string>() ?? "",
                ticket["urgency"]?.GetValue<string>() ?? "Normal",
                ticket["status"]?.GetValue<string>() ?? "Requested",
                DateTime.TryParse(ticket["created_at"]?.GetValue<string>(), out var createdAt) ? createdAt : DateTime.UtcNow));
        }
    }

    public async Task<IActionResult> OnPostAsync(string subject, string category, string urgency, string details)
    {
        var studentId = _db.CurrentStudentId;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(details))
        {
            TempData["FlashMessage"] = "Please fill out all required fields.";
            TempData["FlashType"] = "danger";
            return RedirectToPage();
        }

        var createdAt = DateTime.UtcNow;
        _jsonStore.UpdateRoot(root =>
        {
            var tickets = CanonicalJsonStore.GetOrCreateArray(root, "guidance_requests");
            tickets.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(tickets),
                ["student_id"] = studentId,
                ["subject"] = subject.Trim(),
                ["category"] = category ?? "Academic Counseling",
                ["details"] = details.Trim(),
                ["safety_valve_text"] = null,
                ["urgency"] = urgency is "Urgent" or "Priority" ? urgency : "Normal",
                ["status"] = "Requested",
                ["assigned_counselor_id"] = null,
                ["idempotency_key"] = null,
                ["row_version"] = "AQ==",
                ["created_at"] = createdAt.ToString("O")
            });
        });
        await _db.LogActivityAsync(studentId, $"Submitted Guidance ticket ({category}: {subject}) via Student Portal");

        TempData["FlashMessage"] = "Your guidance request has been submitted. A counselor will reach out via your institutional email.";
        TempData["FlashType"] = "success";
        return RedirectToPage();
    }
}
