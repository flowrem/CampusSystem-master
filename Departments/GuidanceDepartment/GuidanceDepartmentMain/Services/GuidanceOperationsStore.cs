using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace GuidanceDepartmentMain.Services;

public record CaseNoteDto(int Id, int StudentId, string Counselor, string Category, string Content, DateTimeOffset CreatedAt);
public record CreateCaseNoteRequest(int StudentId, string Category, string Content, bool IsConfidential = true);
public record AppointmentDto(int Id, int StudentId, string Counselor, DateTimeOffset AppointmentAt, string Status, string Purpose, string? Remarks);
public record CreateAppointmentRequest(int StudentId, DateTimeOffset AppointmentAt, string Purpose, string? Remarks, string Status = "Scheduled");
public record ClearanceRequest(int StudentId, string SchoolYear, string Semester, string Status, string? Remarks);

public sealed class GuidanceOperationsStore(CanonicalJsonStore store)
{
    public IReadOnlyList<CaseNoteDto> ListNotes(int? studentId) => CanonicalJsonStore.GetArray(store.ReadRoot(), "counseling_case_notes").OfType<JsonObject>()
        .Where(row => !studentId.HasValue || Number(row, "student_id") == studentId).Select(ReadNote).OrderByDescending(note => note.CreatedAt).ToList();

    public CaseNoteDto AddNote(CreateCaseNoteRequest input, string counselor)
    {
        CaseNoteDto? created = null;
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, "counseling_case_notes");
            var row = new JsonObject { ["id"] = CanonicalJsonStore.NextId(rows), ["student_id"] = input.StudentId, ["counselor"] = counselor, ["category"] = input.Category?.Trim() ?? "General Counseling", ["content"] = input.Content.Trim(), ["is_confidential"] = input.IsConfidential, ["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            rows.Add(row); created = ReadNote(row);
        });
        return created!;
    }

    public IReadOnlyList<AppointmentDto> ListAppointments(int? studentId) => CanonicalJsonStore.GetArray(store.ReadRoot(), "guidance_appointments").OfType<JsonObject>()
        .Where(row => !studentId.HasValue || Number(row, "student_id") == studentId).Select(ReadAppointment).OrderBy(appointment => appointment.AppointmentAt).ToList();

    public AppointmentDto AddAppointment(CreateAppointmentRequest input, string counselor)
    {
        AppointmentDto? created = null;
        store.UpdateRoot(root =>
        {
            var rows = CanonicalJsonStore.GetOrCreateArray(root, "guidance_appointments");
            var row = new JsonObject { ["id"] = CanonicalJsonStore.NextId(rows), ["student_id"] = input.StudentId, ["counselor"] = counselor, ["appointment_at"] = input.AppointmentAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), ["status"] = string.IsNullOrWhiteSpace(input.Status) ? "Scheduled" : input.Status.Trim(), ["purpose"] = input.Purpose?.Trim() ?? "General Counseling", ["remarks"] = input.Remarks?.Trim(), ["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            rows.Add(row); created = ReadAppointment(row);
        });
        return created!;
    }

    public void SignClearance(ClearanceRequest input, string clearedBy) => store.UpdateRoot(root =>
    {
        var rows = CanonicalJsonStore.GetOrCreateArray(root, "student_clearance");
        var row = rows.OfType<JsonObject>().FirstOrDefault(item => Number(item, "student_id") == input.StudentId && Text(item, "department_name").Contains("Guidance", StringComparison.OrdinalIgnoreCase) && Text(item, "school_year") == input.SchoolYear && Text(item, "semester") == input.Semester);
        if (row is null) rows.Add(row = new JsonObject { ["id"] = CanonicalJsonStore.NextId(rows), ["student_id"] = input.StudentId, ["department_name"] = "Guidance Office", ["school_year"] = input.SchoolYear, ["semester"] = input.Semester });
        row["status"] = input.Status.Trim(); row["cleared_by"] = clearedBy; row["cleared_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture); row["remarks"] = input.Remarks?.Trim();
    });

    private static CaseNoteDto ReadNote(JsonObject row) => new(Number(row, "id"), Number(row, "student_id"), Text(row, "counselor"), Text(row, "category"), Text(row, "content"), Date(row, "created_at"));
    private static AppointmentDto ReadAppointment(JsonObject row) => new(Number(row, "id"), Number(row, "student_id"), Text(row, "counselor"), Date(row, "appointment_at"), Text(row, "status"), Text(row, "purpose"), Text(row, "remarks"));
    private static int Number(JsonObject row, string field) => row[field] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
    private static string Text(JsonObject row, string field) => row[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    private static DateTimeOffset Date(JsonObject row, string field) => DateTimeOffset.TryParse(Text(row, field), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : DateTimeOffset.MinValue;
}
