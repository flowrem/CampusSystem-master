using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using RegistrarMain.Models;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class DocumentProcessingModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly CanonicalJsonStore _jsonStore;

    public DocumentProcessingModel(DatabaseService db, CanonicalJsonStore jsonStore)
    {
        _db = db;
        _jsonStore = jsonStore;
    }

    public string ActiveTab { get; set; } = "queue";
    public string StatusFilter { get; set; } = "";
    public List<DocumentQueueItem> RequestsQueue { get; set; } = new();
    public List<StudentSimpleItem> StudentsList { get; set; } = new();

    public int PrintReqId { get; set; }
    public PrintDataDetail? PrintData { get; set; }
    public List<PrintStudentGradeItem> PrintStudentGrades { get; set; } = new();

    public class DocumentQueueItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string Purpose { get; set; } = "";
        public int Copies { get; set; }
        public string Status { get; set; } = "pending";
        public string QrCodeToken { get; set; } = "";
        public string? Remarks { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    public class StudentSimpleItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Program { get; set; } = "";
    }

    public class PrintDataDetail
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public decimal? AverageGrade { get; set; }
        public string DocumentType { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string QrCodeToken { get; set; } = "";
        public DateTime RequestedAt { get; set; }
    }

    public class PrintStudentGradeItem
    {
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
        public string SchoolYear { get; set; } = "";
        public string Semester { get; set; } = "";
        public decimal? Grade { get; set; }
        public bool IsInc { get; set; }
        public string? Remarks { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string tab = "queue", string status_filter = "", int req_id = 0)
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "queue", "issue", "print_doc" }.Contains(tab) ? tab : "queue";
        StatusFilter = status_filter?.Trim() ?? "";
        PrintReqId = req_id;

        var root = _jsonStore.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var requests = CanonicalJsonStore.GetArray(root, "transcript_requests").OfType<JsonObject>().ToList();
        StudentsList = users.Where(user => JsonString(user, "role") == "student")
            .Select(user => new StudentSimpleItem
            {
                Id = JsonInt(user, "id"),
                StudentIdNumber = JsonString(user, "student_id_number"),
                Name = JsonString(user, "name"),
                Program = JsonString(profiles.FirstOrDefault(profile => JsonInt(profile, "user_id") == JsonInt(user, "id")), "program")
            }).OrderBy(student => student.Name).ToList();

        RequestsQueue = requests.Select(request =>
        {
            var studentId = JsonInt(request, "student_id");
            var user = users.FirstOrDefault(item => JsonInt(item, "id") == studentId);
            var profile = profiles.FirstOrDefault(item => JsonInt(item, "user_id") == studentId);
            if (user == null) return null;
            return new DocumentQueueItem
            {
                Id = JsonInt(request, "id"),
                StudentId = studentId,
                StudentName = JsonString(user, "name"),
                StudentIdNumber = JsonString(user, "student_id_number"),
                Email = JsonString(user, "email"),
                Program = JsonString(profile, "program"),
                YearLevel = JsonString(profile, "year_level"),
                DocumentType = JsonString(request, "document_type"),
                Purpose = JsonString(request, "purpose"),
                Copies = JsonInt(request, "copies", 1),
                Status = JsonString(request, "processing_status", JsonString(request, "status", "pending")),
                QrCodeToken = JsonString(request, "tracking_token", JsonString(request, "qr_code_token")),
                Remarks = JsonString(request, "remarks"),
                RequestedAt = JsonDate(request, "request_date", JsonDate(request, "requested_at", DateTime.UtcNow))
            };
        }).Where(item => item != null && (string.IsNullOrEmpty(StatusFilter) || string.Equals(item.Status, StatusFilter, StringComparison.OrdinalIgnoreCase)))
          .Cast<DocumentQueueItem>().OrderByDescending(item => item.RequestedAt).ToList();

        if (PrintReqId > 0)
        {
            var request = requests.FirstOrDefault(item => JsonInt(item, "id") == PrintReqId);
            if (request != null)
            {
                var studentId = JsonInt(request, "student_id");
                var user = users.FirstOrDefault(item => JsonInt(item, "id") == studentId);
                var profile = profiles.FirstOrDefault(item => JsonInt(item, "user_id") == studentId);
                if (user != null)
                {
                    PrintData = new PrintDataDetail
                    {
                        Id = PrintReqId,
                        StudentId = studentId,
                        StudentName = JsonString(user, "name"),
                        StudentIdNumber = JsonString(user, "student_id_number"),
                        Email = JsonString(user, "email"),
                        Program = JsonString(profile, "program"),
                        YearLevel = JsonString(profile, "year_level"),
                        AverageGrade = profile != null && profile["average_grade"] != null ? profile["average_grade"]!.GetValue<decimal>() : null,
                        DocumentType = JsonString(request, "document_type"),
                        Purpose = JsonString(request, "purpose"),
                        QrCodeToken = JsonString(request, "tracking_token", JsonString(request, "qr_code_token")),
                        RequestedAt = JsonDate(request, "request_date", JsonDate(request, "requested_at", DateTime.UtcNow))
                    };
                }
            }

            if (PrintData != null)
            {
                var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
                var enrollmentRows = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
                var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();
                PrintStudentGrades = enrolledSubjects.Where(item => JsonInt(item, "student_id") == PrintData.StudentId)
                    .Select(item =>
                    {
                        var grade = grades.FirstOrDefault(record => JsonInt(record, "enrolled_subject_id") == JsonInt(item, "id"));
                        var enrollment = enrollmentRows.FirstOrDefault(record => JsonInt(record, "id") == JsonInt(item, "enrollment_id"));
                        return new PrintStudentGradeItem
                        {
                            SubjectCode = JsonString(item, "subject_code"),
                            SubjectName = JsonString(item, "subject_name"),
                            Units = JsonInt(item, "units"),
                            SchoolYear = JsonString(enrollment, "school_year"),
                            Semester = JsonString(enrollment, "semester"),
                            Grade = grade != null && grade["final_grade"] != null ? grade["final_grade"]!.GetValue<decimal>() : null,
                            IsInc = grade != null && grade["is_inc"]?.GetValue<bool>() == true,
                            Remarks = JsonString(grade, "remarks")
                        };
                    }).OrderBy(item => item.SchoolYear).ThenBy(item => item.Semester).ThenBy(item => item.SubjectCode).ToList();
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateRequestAsync(int student_id, string document_type, string purpose, int copies, string? remarks)
    {
        _db.EnsureRegistrarSession();
        var docType = (document_type ?? "").Trim();
        var p = (purpose ?? "").Trim();

        if (student_id > 0 && !string.IsNullOrEmpty(docType))
        {
            var letters = Regex.Replace(docType, "[^A-Za-z]", "").ToUpper();
            var prefix = letters.Length >= 3 ? letters.Substring(0, 3) : "DOC";
            var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var qrToken = $"NU-{prefix}-{DateTime.Now.Year}-{randomHex}";

            _jsonStore.UpdateRoot(root =>
            {
                var requests = CanonicalJsonStore.GetOrCreateArray(root, "transcript_requests");
                var nextId = requests.OfType<JsonObject>().Select(item => JsonInt(item, "id")).DefaultIfEmpty(0).Max() + 1;
                var requestedAt = DateTime.UtcNow.ToString("O");
                requests.Add(new JsonObject
                {
                    ["id"] = nextId,
                    ["student_id"] = student_id,
                    ["document_type"] = docType,
                    ["purpose"] = p,
                    ["copies"] = Math.Max(1, copies),
                    ["status"] = "pending",
                    ["processing_status"] = "pending",
                    ["tracking_token"] = qrToken,
                    ["qr_code_token"] = qrToken,
                    ["remarks"] = (remarks ?? "").Trim(),
                    ["request_date"] = requestedAt,
                    ["requested_at"] = requestedAt
                });
            });

            await _db.LogActivityAsync(student_id, $"Document request for {docType} created (Token: {qrToken}) by {_db.CurrentUserName}.");
            TempData["FlashMessage"] = $"Document request successfully logged with Tracking Token: {qrToken}";
            TempData["FlashType"] = "success";
        }
        else
        {
            TempData["FlashMessage"] = "Please select student and document type.";
            TempData["FlashType"] = "error";
        }

        return RedirectToPage("/DocumentProcessing", new { tab = "queue" });
    }

    public IActionResult OnPostUpdateStatus(int request_id, string status, string? remarks)
    {
        _db.EnsureRegistrarSession();
        var validStatuses = new[] { "pending", "processing", "ready", "released", "rejected" };
        var newStatus = (status ?? "").Trim().ToLower();

        if (request_id > 0 && validStatuses.Contains(newStatus))
        {
            var updated = false;
            _jsonStore.UpdateRoot(root =>
            {
                var request = CanonicalJsonStore.GetArray(root, "transcript_requests").OfType<JsonObject>().FirstOrDefault(item => JsonInt(item, "id") == request_id);
                if (request == null) return;
                var studentId = JsonInt(request, "student_id");
                var documentType = JsonString(request, "document_type");
                request["status"] = newStatus;
                request["processing_status"] = newStatus;
                request["remarks"] = (remarks ?? "").Trim();
                if (newStatus == "processing") request["processed_at"] = DateTime.UtcNow.ToString("O");
                if (newStatus == "released") request["released_at"] = DateTime.UtcNow.ToString("O");
                AppendActivity(root, studentId, $"Document request #{request_id} ({documentType}) marked as {newStatus} by {_db.CurrentUserName}.");
                updated = true;
            });

            TempData["FlashMessage"] = updated ? $"Request #{request_id} status updated to {char.ToUpper(newStatus[0]) + newStatus.Substring(1)}." : "The document request was not found.";
            TempData["FlashType"] = updated ? "success" : "error";
        }

        return RedirectToPage("/DocumentProcessing", new { tab = "queue" });
    }

    private static string JsonString(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int JsonInt(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static DateTime JsonDate(JsonObject? item, string property, DateTime fallback) =>
        DateTime.TryParse(JsonString(item, property), out var value) ? value : fallback;

    private void AppendActivity(JsonObject root, int? userId, string message)
    {
        var activities = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
        var nextId = activities.OfType<JsonObject>().Select(item => JsonInt(item, "id")).DefaultIfEmpty(0).Max() + 1;
        activities.Add(new JsonObject
        {
            ["id"] = nextId,
            ["user_id"] = userId,
            ["user_name"] = _db.CurrentUserName,
            ["message"] = message,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        });
    }
}
