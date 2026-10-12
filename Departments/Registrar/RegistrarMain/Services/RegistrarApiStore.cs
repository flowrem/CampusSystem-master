using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using RegistrarMain.Contracts;

namespace RegistrarMain.Services;

/// <summary>Raised for a request the API should answer with a specific HTTP status.</summary>
public sealed class RegistrarApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// The student-facing REST API's view of the shared JSON datastore.
/// Courses are <c>subjects</c>, registrations are <c>enrolled_subjects</c>, the transcript is <c>grades</c>,
/// and records or verification requests are <c>transcript_requests</c>.
/// </summary>
public sealed class RegistrarApiStore(CanonicalJsonStore store)
{
    public const string VerificationDocument = "Enrollment Verification";

    // ── Students ────────────────────────────────────────────────────────────

    public bool StudentExists(int studentId) =>
        Rows(store.ReadRoot(), "users").Any(u => Int(u, "id") == studentId && Same(Str(u, "role"), "student"));

    // ── Courses ─────────────────────────────────────────────────────────────

    public IReadOnlyList<CourseDto> GetCourses(string? search)
    {
        var courses = Rows(store.ReadRoot(), "subjects").Select(ToCourse);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            courses = courses.Where(c => c.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                                         || c.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return courses.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public CourseDto? GetCourse(int id) =>
        Rows(store.ReadRoot(), "subjects").Where(s => Int(s, "id") == id).Select(ToCourse).FirstOrDefault();

    public CourseDto CreateCourse(CreateCourseRequest request)
    {
        CourseDto? created = null;
        var code = request.Code.Trim().ToUpperInvariant();
        store.UpdateRoot(root =>
        {
            var subjects = CanonicalJsonStore.GetOrCreateArray(root, "subjects");
            if (subjects.OfType<JsonObject>().Any(s => Same(Str(s, "subject_code"), code)))
                throw new RegistrarApiException(409, $"Course {code} already exists.");

            var row = new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(subjects),
                ["subject_code"] = code,
                ["subject_name"] = request.Title.Trim(),
                ["units"] = request.Credits,
                ["prerequisites"] = new JsonArray()
            };
            subjects.Add(row);
            created = ToCourse(row);
        });
        return created!;
    }

    // ── Registrations (enrolled_subjects) ───────────────────────────────────

    public IReadOnlyList<EnrollmentDto> GetRegistrations(int studentId)
    {
        var root = store.ReadRoot();
        return Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "student_id") == studentId && !Same(Str(e, "status"), "dropped"))
            .Select(e => ToRegistration(root, e))
            .OrderBy(e => e.Semester, StringComparer.Ordinal)
            .ThenBy(e => e.CourseCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Enrolls the student in the course's first open class offering for the term. The semester may be "1st Semester" or "2025-2026 1st Semester".</summary>
    public EnrollmentDto Register(int studentId, EnrollmentRequestDto request)
    {
        var createdId = 0;
        store.UpdateRoot(root =>
        {
            var subject = Rows(root, "subjects").FirstOrDefault(s => Int(s, "id") == request.CourseId)
                ?? throw new RegistrarApiException(404, "Course not found.");

            var enrollment = Rows(root, "enrollments").Where(e => Int(e, "student_id") == studentId)
                .FirstOrDefault(e => TermMatches(e, request.Semester))
                ?? throw new RegistrarApiException(404, "The student has no enrollment for that semester.");

            var enrolled = CanonicalJsonStore.GetOrCreateArray(root, "enrolled_subjects");
            var enrollmentId = Int(enrollment, "id");
            if (enrolled.OfType<JsonObject>().Any(e => Int(e, "enrollment_id") == enrollmentId
                    && Same(Str(e, "subject_code"), Str(subject, "subject_code")) && !Same(Str(e, "status"), "dropped")))
                throw new RegistrarApiException(409, "The student is already enrolled in this course for the semester.");

            var offerings = Rows(root, "class_offerings").Where(o =>
                Int(o, "subject_id") == request.CourseId
                && Same(Str(o, "school_year"), Str(enrollment, "school_year"))
                && Same(Str(o, "semester"), Str(enrollment, "semester"))).ToList();
            if (offerings.Count == 0)
                throw new RegistrarApiException(404, "No class offering exists for that course in the semester.");

            var offering = offerings.FirstOrDefault(o => Int(o, "max_capacity") <= 0 || Int(o, "enrolled_count") < Int(o, "max_capacity"))
                ?? throw new RegistrarApiException(409, "Every section of that course is full.");

            var row = new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(enrolled),
                ["enrollment_id"] = enrollmentId,
                ["student_id"] = studentId,
                ["class_offering_id"] = Int(offering, "id"),
                ["subject_code"] = Str(subject, "subject_code"),
                ["subject_name"] = Str(subject, "subject_name"),
                ["units"] = Dec(subject, "units"),
                ["status"] = "enrolled"
            };
            enrolled.Add(row);
            offering["enrolled_count"] = Int(offering, "enrolled_count") + 1;
            FeeCalculator.Recalculate(root, enrollmentId, $"Registered {Str(subject, "subject_code")} through the API");
            createdId = Int(row, "id");
        });

        // Read it back so the version matches what a later DELETE will compute.
        var saved = store.ReadRoot();
        return ToRegistration(saved, Rows(saved, "enrolled_subjects").First(e => Int(e, "id") == createdId));
    }

    /// <summary>Drops a registration. The row stays as history with status "dropped". The version must match the one last returned.</summary>
    public void Drop(int studentId, int registrationId, string rowVersion)
    {
        store.UpdateRoot(root =>
        {
            var row = Rows(root, "enrolled_subjects").FirstOrDefault(e =>
                    Int(e, "id") == registrationId && Int(e, "student_id") == studentId && !Same(Str(e, "status"), "dropped"))
                ?? throw new RegistrarApiException(409, "The enrollment was already removed or is no longer owned by this student.");

            if (!string.Equals(VersionOf(row), rowVersion, StringComparison.Ordinal))
                throw new RegistrarApiException(409, "The enrollment changed before it could be dropped. Refresh and try again.");

            row["status"] = "dropped";
            if (Rows(root, "class_offerings").FirstOrDefault(o => Int(o, "id") == Int(row, "class_offering_id")) is { } offering)
                offering["enrolled_count"] = Math.Max(0, Int(offering, "enrolled_count") - 1);
            FeeCalculator.Recalculate(root, Int(row, "enrollment_id"), $"Dropped {Str(row, "subject_code")} through the API");
        });
    }

    // ── Transcript (grades) ─────────────────────────────────────────────────

    public TranscriptDto GetTranscript(int studentId)
    {
        var entries = Rows(store.ReadRoot(), "grades")
            // Drafts are not official until the Registrar verifies them.
            .Where(g => Int(g, "student_id") == studentId && !Same(Str(g, "status"), "draft")
                        && (Bool(g, "is_inc") || g["final_grade"] is JsonValue))
            .Select(g => (Semester: $"{Str(g, "school_year")} {Str(g, "semester")}".Trim(),
                          Entry: new TranscriptEntryDto(Str(g, "subject_code"),
                              Bool(g, "is_inc") ? "INC" : Dec(g, "final_grade").ToString("0.00", CultureInfo.InvariantCulture))))
            .OrderBy(g => g.Semester, StringComparer.Ordinal)
            .ThenBy(g => g.Entry.CourseCode, StringComparer.Ordinal)
            .ToList();

        return new TranscriptDto(entries.GroupBy(g => g.Semester)
            .Select(group => new TranscriptSemesterDto(group.Key, group.Select(g => g.Entry).ToList()))
            .ToList());
    }

    // ── Document and verification requests (transcript_requests) ────────────

    public IReadOnlyList<RecordsResponseDto> GetRecordsRequests(int studentId) =>
        StudentRequests(studentId).Where(r => !Same(Str(r, "document_type"), VerificationDocument))
            .Select(ToRecordsResponse).ToList();

    public RecordsResponseDto CreateRecordsRequest(int studentId, string documentType) =>
        ToRecordsResponse(AddRequest(studentId, documentType.Trim()));

    public IReadOnlyList<VerificationResponseDto> GetVerifications(int studentId) =>
        StudentRequests(studentId).Where(r => Same(Str(r, "document_type"), VerificationDocument))
            .Select(r => new VerificationResponseDto(Int(r, "id"), StatusOf(r), RequestedAt(r))).ToList();

    public VerificationResponseDto CreateVerification(int studentId, string? purpose)
    {
        var row = AddRequest(studentId, VerificationDocument, purpose);
        return new VerificationResponseDto(Int(row, "id"), StatusOf(row), RequestedAt(row));
    }

    private IEnumerable<JsonObject> StudentRequests(int studentId) =>
        Rows(store.ReadRoot(), "transcript_requests")
            .Where(r => Int(r, "student_id") == studentId)
            .OrderByDescending(r => RequestedAt(r));

    /// <summary>Same row shape Registrar's Document Processing page writes, so the queue shows it.</summary>
    private JsonObject AddRequest(int studentId, string documentType, string? purpose = null)
    {
        JsonObject? created = null;
        store.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "transcript_requests");
            var token = $"NU-{Prefix(documentType)}-{DateTime.UtcNow.Year}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            created = new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(requests),
                ["student_id"] = studentId,
                ["document_type"] = documentType,
                ["purpose"] = purpose ?? "",
                ["copies"] = 1,
                ["status"] = "pending",
                ["processing_status"] = "pending",
                ["tracking_token"] = token,
                ["qr_code_token"] = token,
                ["request_date"] = now,
                ["requested_at"] = now
            };
            requests.Add(created);
        });
        return created!;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static CourseDto ToCourse(JsonObject s) =>
        new(Int(s, "id"), Str(s, "subject_code"), Str(s, "subject_name"), (int)Math.Round(Dec(s, "units")));

    private static EnrollmentDto ToRegistration(JsonObject root, JsonObject e)
    {
        var enrollment = Rows(root, "enrollments").FirstOrDefault(x => Int(x, "id") == Int(e, "enrollment_id"));
        var offering = Rows(root, "class_offerings").FirstOrDefault(o => Int(o, "id") == Int(e, "class_offering_id"));
        return new EnrollmentDto(
            Int(e, "id"),
            offering is null ? 0 : Int(offering, "subject_id"),
            Str(e, "subject_code"),
            enrollment is null ? "" : $"{Str(enrollment, "school_year")} {Str(enrollment, "semester")}".Trim(),
            VersionOf(e));
    }

    private static RecordsResponseDto ToRecordsResponse(JsonObject r) =>
        new(Int(r, "id"), Str(r, "document_type"), StatusOf(r), RequestedAt(r));

    private static string StatusOf(JsonObject r)
    {
        var status = Str(r, "processing_status");
        if (status.Length == 0) status = Str(r, "status");
        return status.Length == 0 ? "Pending" : char.ToUpperInvariant(status[0]) + status[1..];
    }

    private static DateTime RequestedAt(JsonObject r) =>
        DateTimeOffset.TryParse(Str(r, "request_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : default;

    private static string Prefix(string documentType)
    {
        var letters = new string(documentType.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return letters.Length >= 3 ? letters[..3] : "DOC";
    }

    /// <summary>A short fingerprint of the row. It changes whenever the row does, standing in for SQL's rowversion.</summary>
    private static string VersionOf(JsonObject row) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(row.ToJsonString()))[..8]);

    private static bool TermMatches(JsonObject enrollment, string semester)
    {
        var requested = semester.Trim();
        var term = Str(enrollment, "semester");
        return Same(requested, term) || Same(requested, $"{Str(enrollment, "school_year")} {term}");
    }

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        CanonicalJsonStore.GetArray(root, collection).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int Int(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<long>(out var l) ? (int)l
        : v.TryGetValue<decimal>(out var d) ? (int)d
        : 0;

    private static decimal Dec(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0m
        : v.TryGetValue<decimal>(out var d) ? d
        : v.TryGetValue<double>(out var f) ? (decimal)f
        : v.TryGetValue<long>(out var l) ? l
        : v.TryGetValue<int>(out var n) ? n
        : 0m;

    private static string Str(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static bool Bool(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
