using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Models;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class ClassSchedulingModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CanonicalJsonStore _store;

    public ClassSchedulingModel(DatabaseService db, SettingsService settings, CanonicalJsonStore store)
    {
        _db = db;
        _settings = settings;
        _store = store;
    }

    public static readonly string[] AllDays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string CurrentSemester { get; set; } = "1st Semester";
    public string ActiveTab { get; set; } = "offerings";
    public List<SubjectItem> SubjectsList { get; set; } = new();
    public List<ClassOfferingItem> OfferingsList { get; set; } = new();
    public int SelectedOfferingId { get; set; }
    public ClassOfferingItem? SelectedOfferingDetails { get; set; }
    public List<RosterStudentItem> RosterStudents { get; set; } = new();

    public class SubjectItem
    {
        public int Id { get; set; }
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
        public int LecHours { get; set; }
        public int LabHours { get; set; }
        public string? PrereqCode { get; set; }
        public string CurriculumProgram { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string Semester { get; set; } = "";
    }

    public class ClassOfferingItem
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectCode { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public decimal Units { get; set; }
        public string SectionCode { get; set; } = "";
        public string Room { get; set; } = "";
        public string DaysOfWeek { get; set; } = "";
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string InstructorName { get; set; } = "";
        public int Capacity { get; set; }
        public int SlotsTaken { get; set; }
        public string Status { get; set; } = "open";
    }

    public class RosterStudentItem
    {
        public int EnrolledSubId { get; set; }
        public string SubjectStatus { get; set; } = "";
        public int StudentId { get; set; }
        public string Name { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public decimal? Grade { get; set; }
        public bool IsInc { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string tab = "offerings", int offering_id = 0)
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "offerings", "subjects", "roster" }.Contains(tab) ? tab : "offerings";
        CurrentSchoolYear = await _settings.GetSchoolYearAsync();
        CurrentSemester = await _settings.GetSemesterAsync();

        var root = _store.ReadRoot();
        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
        var offerings = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().ToList();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var enrollments = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().ToList();
        var enrolledSubjects = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().ToList();
        var grades = CanonicalJsonStore.GetArray(root, "grades").OfType<JsonObject>().ToList();

        SubjectsList = subjects.Select(subject => new SubjectItem
        {
            Id = IntValue(subject, "id"),
            SubjectCode = StringValue(subject, "subject_code"),
            SubjectName = StringValue(subject, "subject_name"),
            Units = DecimalValue(subject, "units"),
            LecHours = IntValue(subject, "lec_hours"),
            LabHours = IntValue(subject, "lab_hours"),
            PrereqCode = ReadPrerequisite(subject),
            CurriculumProgram = StringValue(subject, "curriculum_program", "All"),
            YearLevel = StringValue(subject, "year_level"),
            Semester = StringValue(subject, "semester")
        }).OrderBy(subject => subject.SubjectCode).ToList();

        OfferingsList = offerings.Select(offering => MapOffering(offering, subjects))
            .OrderBy(offering => offering.SectionCode).ThenBy(offering => offering.SubjectCode).ToList();
        SelectedOfferingId = offering_id > 0 ? offering_id : OfferingsList.FirstOrDefault()?.Id ?? 0;
        SelectedOfferingDetails = OfferingsList.FirstOrDefault(offering => offering.Id == SelectedOfferingId);

        if (SelectedOfferingDetails != null)
        {
            RosterStudents = enrolledSubjects.Where(item => IntValue(item, "class_offering_id") == SelectedOfferingId && StringValue(item, "status") == "enrolled")
                .Select(item =>
                {
                    var studentId = IntValue(item, "student_id");
                    var enrollment = enrollments.FirstOrDefault(entry => IntValue(entry, "id") == IntValue(item, "enrollment_id"));
                    var user = users.FirstOrDefault(entry => IntValue(entry, "id") == studentId);
                    var profile = profiles.FirstOrDefault(entry => IntValue(entry, "user_id") == studentId);
                    var grade = grades.FirstOrDefault(entry => IntValue(entry, "enrolled_subject_id") == IntValue(item, "id"));
                    if (user == null || enrollment == null) return null;
                    return new RosterStudentItem
                    {
                        EnrolledSubId = IntValue(item, "id"),
                        SubjectStatus = StringValue(item, "status"),
                        StudentId = studentId,
                        Name = StringValue(user, "name"),
                        StudentIdNumber = StringValue(user, "student_id_number"),
                        Email = StringValue(user, "email"),
                        Program = StringValue(profile, "program"),
                        YearLevel = StringValue(profile, "year_level"),
                        Grade = grade != null && grade["final_grade"] != null ? grade["final_grade"]!.GetValue<decimal>() : null,
                        IsInc = grade?["is_inc"]?.GetValue<bool>() == true
                    };
                }).Where(item => item != null).Cast<RosterStudentItem>().OrderBy(item => item.Name).ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddSubjectAsync(string subject_code, string subject_name, decimal units, int lec_hours, int lab_hours, int? prerequisite_subject_id, string curriculum_program, string year_level, string semester)
    {
        _db.EnsureRegistrarSession();
        var code = (subject_code ?? "").Trim().ToUpperInvariant();
        var name = (subject_name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return SetMessage("Subject code and descriptive title are required.", "error", "subjects");

        var duplicate = false;
        _store.UpdateRoot(root =>
        {
            var subjects = CanonicalJsonStore.GetOrCreateArray(root, "subjects");
            if (subjects.OfType<JsonObject>().Any(item => StringValue(item, "subject_code").Equals(code, StringComparison.OrdinalIgnoreCase)))
            {
                duplicate = true;
                return;
            }
            var prereq = subjects.OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == (prerequisite_subject_id ?? 0));
            var subject = new JsonObject
            {
                ["id"] = NextId(subjects),
                ["subject_code"] = code,
                ["subject_name"] = name,
                ["units"] = units,
                ["lec_hours"] = lec_hours,
                ["lab_hours"] = lab_hours,
                ["curriculum_program"] = curriculum_program,
                ["year_level"] = year_level,
                ["semester"] = semester
            };
            if (prereq != null) subject["prerequisites"] = new JsonArray(JsonValue.Create(StringValue(prereq, "subject_code")));
            else subject["prerequisites"] = new JsonArray();
            subjects.Add(subject);
        });

        if (duplicate) return SetMessage($"Subject {code} already exists.", "error", "subjects");
        await _db.LogActivityAsync(_db.CurrentUserId, $"Subject {code} ({name}) added to catalog by {_db.CurrentUserName}.");
        return SetMessage($"Subject {code} added successfully.", "success", "subjects");
    }

    public IActionResult OnPostDeleteSubject(int subject_id)
    {
        _db.EnsureRegistrarSession();
        var deleted = false;
        _store.UpdateRoot(root =>
        {
            var offerings = CanonicalJsonStore.GetArray(root, "class_offerings");
            if (offerings.OfType<JsonObject>().Any(item => IntValue(item, "subject_id") == subject_id)) return;
            var subjects = CanonicalJsonStore.GetArray(root, "subjects");
            for (var index = 0; index < subjects.Count; index++)
            {
                if (subjects[index] is JsonObject subject && IntValue(subject, "id") == subject_id)
                {
                    subjects.RemoveAt(index);
                    deleted = true;
                    break;
                }
            }
        });
        return deleted
            ? SetMessage("Subject deleted from catalog.", "success", "subjects")
            : SetMessage("Cannot delete a subject used by a class offering, or the subject was not found.", "error", "subjects");
    }

    public async Task<IActionResult> OnPostAddOfferingAsync(int subject_id, string section_code, string room, int capacity, string instructor_name, string[] days, string start_time, string end_time)
    {
        _db.EnsureRegistrarSession();
        CurrentSchoolYear = await _settings.GetSchoolYearAsync();
        CurrentSemester = await _settings.GetSemesterAsync();
        var section = (section_code ?? "").Trim().ToUpperInvariant();
        var roomName = (room ?? "").Trim().ToUpperInvariant();
        var validDays = (days ?? Array.Empty<string>()).Where(day => AllDays.Contains(day)).Distinct().ToArray();
        if (subject_id <= 0 || string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(roomName) || validDays.Length == 0 ||
            !TimeSpan.TryParse(start_time, out var start) || !TimeSpan.TryParse(end_time, out var end) || start >= end || capacity <= 0)
            return SetMessage("Please enter valid subject, section, room, days, time, and capacity.", "error", "offerings");

        var error = "";
        _store.UpdateRoot(root =>
        {
            var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
            if (subjects.All(subject => IntValue(subject, "id") != subject_id))
            {
                error = "The selected subject was not found.";
                return;
            }
            var offerings = CanonicalJsonStore.GetOrCreateArray(root, "class_offerings");
            var conflict = offerings.OfType<JsonObject>().FirstOrDefault(offering =>
            {
                if (StringValue(offering, "room").Equals(roomName, StringComparison.OrdinalIgnoreCase) == false ||
                    StringValue(offering, "school_year") != CurrentSchoolYear || StringValue(offering, "semester") != CurrentSemester ||
                    StringValue(offering, "status", "open") == "cancelled") return false;
                if (!TimeSpan.TryParse(StringValue(offering, "start_time"), out var otherStart) || !TimeSpan.TryParse(StringValue(offering, "end_time"), out var otherEnd)) return false;
                var overlaps = start < otherEnd && end > otherStart;
                var sharedDay = StringValue(offering, "days_of_week").Split(',').Select(item => item.Trim()).Intersect(validDays).Any();
                return overlaps && sharedDay;
            });
            if (conflict != null)
            {
                error = $"Room {roomName} conflicts with section {StringValue(conflict, "section_code")} during the selected time.";
                return;
            }
            offerings.Add(new JsonObject
            {
                ["id"] = NextId(offerings),
                ["subject_id"] = subject_id,
                ["section_code"] = section,
                ["school_year"] = CurrentSchoolYear,
                ["semester"] = CurrentSemester,
                ["room"] = roomName,
                ["days_of_week"] = string.Join(",", validDays),
                ["start_time"] = start.ToString("c"),
                ["end_time"] = end.ToString("c"),
                ["instructor_name"] = (instructor_name ?? "Prof. TBD").Trim(),
                ["max_capacity"] = capacity,
                ["enrolled_count"] = 0,
                ["status"] = "open"
            });
        });
        if (!string.IsNullOrEmpty(error)) return SetMessage(error, "error", "offerings");
        await _db.LogActivityAsync(_db.CurrentUserId, $"Class offering {section} created for {CurrentSchoolYear} by {_db.CurrentUserName}.");
        return SetMessage($"Class offering {section} successfully scheduled.", "success", "offerings");
    }

    public IActionResult OnPostToggleOffering(int offering_id)
    {
        _db.EnsureRegistrarSession();
        var changed = false;
        _store.UpdateRoot(root =>
        {
            var offering = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().FirstOrDefault(item => IntValue(item, "id") == offering_id);
            if (offering == null) return;
            var status = StringValue(offering, "status", "open");
            offering["status"] = status == "open" ? "closed" : "open";
            changed = true;
        });
        return changed ? SetMessage("Offering status updated.", "success", "offerings") : SetMessage("Offering not found.", "error", "offerings");
    }

    public IActionResult OnPostDeleteOffering(int offering_id)
    {
        _db.EnsureRegistrarSession();
        var deleted = false;
        _store.UpdateRoot(root =>
        {
            if (CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().Any(item => IntValue(item, "class_offering_id") == offering_id)) return;
            var offerings = CanonicalJsonStore.GetArray(root, "class_offerings");
            for (var index = 0; index < offerings.Count; index++)
            {
                if (offerings[index] is JsonObject item && IntValue(item, "id") == offering_id)
                {
                    offerings.RemoveAt(index);
                    deleted = true;
                    break;
                }
            }
        });
        return deleted
            ? SetMessage("Class offering deleted.", "success", "offerings")
            : SetMessage("Cannot delete an offering referenced by an enrollment, or it was not found.", "error", "offerings");
    }

    private IActionResult SetMessage(string message, string type, string tab)
    {
        TempData["FlashMessage"] = message;
        TempData["FlashType"] = type;
        return RedirectToPage("/ClassScheduling", new { tab });
    }

    private static ClassOfferingItem MapOffering(JsonObject offering, IReadOnlyList<JsonObject> subjects)
    {
        var subject = subjects.FirstOrDefault(item => IntValue(item, "id") == IntValue(offering, "subject_id"));
        return new ClassOfferingItem
        {
            Id = IntValue(offering, "id"),
            SubjectId = IntValue(offering, "subject_id"),
            SubjectCode = StringValue(subject, "subject_code"),
            SubjectName = StringValue(subject, "subject_name"),
            Units = DecimalValue(subject, "units"),
            SectionCode = StringValue(offering, "section_code"),
            Room = StringValue(offering, "room"),
            DaysOfWeek = StringValue(offering, "days_of_week"),
            StartTime = TimeSpan.TryParse(StringValue(offering, "start_time"), out var start) ? start : TimeSpan.Zero,
            EndTime = TimeSpan.TryParse(StringValue(offering, "end_time"), out var end) ? end : TimeSpan.Zero,
            InstructorName = StringValue(offering, "instructor_name"),
            Capacity = IntValue(offering, "max_capacity"),
            SlotsTaken = IntValue(offering, "enrolled_count"),
            Status = StringValue(offering, "status", "open")
        };
    }

    private static string ReadPrerequisite(JsonObject subject)
    {
        if (subject["prerequisites"] is JsonArray prerequisites) return prerequisites.FirstOrDefault()?.GetValue<string>() ?? "";
        return subject["prerequisites"]?.GetValue<string>() ?? "";
    }

    private static int NextId(JsonArray items) => items.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
    private static string StringValue(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static int IntValue(JsonObject? item, string property, int fallback = 0) => item?[property]?.GetValue<int>() ?? fallback;
    private static decimal DecimalValue(JsonObject? item, string property) => item?[property]?.GetValue<decimal>() ?? 0m;
}
