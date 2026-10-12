using System.Text;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

public class GovernmentComplianceModel : PageModel
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CanonicalJsonStore _store;

    public GovernmentComplianceModel(DatabaseService db, SettingsService settings, CanonicalJsonStore store)
    {
        _db = db;
        _settings = settings;
        _store = store;
    }

    public string CurrentSchoolYear { get; set; } = "2025-2026";
    public string CurrentSemester { get; set; } = "1st Semester";
    public string SchoolName { get; set; } = "National University Lipa";
    public string SchoolCode { get; set; } = "NU-LIPA-04";
    public string ActiveTab { get; set; } = "so";
    public List<StudentDropdownItem> GraduatingStudents { get; set; } = new();
    public List<ChedSoItem> SoRecords { get; set; } = new();
    public List<NstpSerialItem> NstpRecords { get; set; } = new();
    public List<MasterlistItem> EnrolledMasterlist { get; set; } = new();

    public class StudentDropdownItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Program { get; set; } = "";
    }

    public class ChedSoItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string SoNumber { get; set; } = "";
        public string SeriesYear { get; set; } = "";
        public string Program { get; set; } = "";
        public DateTime DateApplied { get; set; }
        public DateTime? DateIssued { get; set; }
        public string Status { get; set; } = "Applied";
        public string? Remarks { get; set; }
    }

    public class NstpSerialItem
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = "";
        public string StudentIdNumber { get; set; } = "";
        public string Program { get; set; } = "";
        public string NstpComponent { get; set; } = "CWTS";
        public string SerialNumber { get; set; } = "";
        public DateTime DateIssued { get; set; }
        public string? Remarks { get; set; }
    }

    public class MasterlistItem
    {
        public int Id { get; set; }
        public string StudentIdNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Program { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public int TotalUnits { get; set; }
        public string EnrollmentType { get; set; } = "";
        public string EnrollmentStatus { get; set; } = "active";
    }

    public async Task<IActionResult> OnGetAsync(string tab = "so", string? export = null)
    {
        _db.EnsureRegistrarSession();
        ActiveTab = new[] { "so", "nstp", "masterlist" }.Contains(tab) ? tab : "so";
        CurrentSchoolYear = await _settings.GetSchoolYearAsync();
        CurrentSemester = await _settings.GetSemesterAsync();
        SchoolName = await _db.GetSettingAsync("school_name", "National University Lipa");
        SchoolCode = await _db.GetSettingAsync("school_code", "NU-LIPA-04");

        var root = _store.ReadRoot();
        var users = CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().Where(item => StringValue(item, "role") == "student").ToList();
        var profiles = CanonicalJsonStore.GetArray(root, "student_profiles").OfType<JsonObject>().ToList();
        var userById = users.ToDictionary(item => IntValue(item, "id"));
        var profileById = profiles.ToDictionary(item => IntValue(item, "user_id"));

        GraduatingStudents = users.Select(user =>
        {
            var studentId = IntValue(user, "id");
            profileById.TryGetValue(studentId, out var profile);
            return new StudentDropdownItem
            {
                Id = studentId,
                StudentIdNumber = StringValue(user, "student_id_number"),
                Name = StringValue(user, "name"),
                Program = StringValue(profile, "program")
            };
        }).OrderBy(item => item.Name).ToList();

        SoRecords = ReadRows(root, "ched_special_orders").Select(record =>
        {
            var studentId = IntValue(record, "student_id");
            userById.TryGetValue(studentId, out var user);
            return new ChedSoItem
            {
                Id = IntValue(record, "id"),
                StudentId = studentId,
                StudentName = StringValue(user, "name"),
                StudentIdNumber = StringValue(user, "student_id_number"),
                Email = StringValue(user, "email"),
                SoNumber = StringValue(record, "so_number"),
                SeriesYear = StringValue(record, "series_year"),
                Program = StringValue(record, "program"),
                DateApplied = DateValue(record, "date_applied", DateTime.UtcNow),
                DateIssued = NullableDateValue(record, "date_issued"),
                Status = StringValue(record, "status", "Applied"),
                Remarks = StringValue(record, "remarks")
            };
        }).OrderByDescending(item => item.DateApplied).ToList();

        NstpRecords = ReadRows(root, "nstp_serial_numbers").Select(record =>
        {
            var studentId = IntValue(record, "student_id");
            userById.TryGetValue(studentId, out var user);
            profileById.TryGetValue(studentId, out var profile);
            return new NstpSerialItem
            {
                Id = IntValue(record, "id"),
                StudentId = studentId,
                StudentName = StringValue(user, "name"),
                StudentIdNumber = StringValue(user, "student_id_number"),
                Program = StringValue(profile, "program"),
                NstpComponent = StringValue(record, "nstp_component", "CWTS"),
                SerialNumber = StringValue(record, "serial_number"),
                DateIssued = DateValue(record, "date_issued", DateTime.UtcNow),
                Remarks = StringValue(record, "remarks")
            };
        }).OrderByDescending(item => item.DateIssued).ToList();

        EnrolledMasterlist = CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>()
            .Where(enrollment => StringValue(enrollment, "school_year") == CurrentSchoolYear && StringValue(enrollment, "semester") == CurrentSemester &&
                new[] { "active", "registered", "enrolled" }.Contains(StringValue(enrollment, "status"), StringComparer.OrdinalIgnoreCase))
            .Select(enrollment =>
            {
                var studentId = IntValue(enrollment, "student_id");
                userById.TryGetValue(studentId, out var user);
                profileById.TryGetValue(studentId, out var profile);
                if (user == null) return null;
                return new MasterlistItem
                {
                    Id = IntValue(enrollment, "id"),
                    StudentIdNumber = StringValue(user, "student_id_number"),
                    Name = StringValue(user, "name"),
                    Gender = StringValue(profile, "gender"),
                    Program = StringValue(profile, "program"),
                    YearLevel = StringValue(profile, "year_level"),
                    TotalUnits = IntValue(enrollment, "total_units"),
                    EnrollmentType = StringValue(enrollment, "enrollment_type", "Regular"),
                    EnrollmentStatus = StringValue(enrollment, "status")
                };
            }).Where(item => item != null).Cast<MasterlistItem>().OrderBy(item => item.Program).ThenBy(item => item.Name).ToList();

        if (export == "csv")
        {
            var csv = new StringBuilder();
            csv.AppendLine($"\"CHED Institutional Code\",\"{Csv(SchoolCode)}\"");
            csv.AppendLine($"\"Higher Education Institution\",\"{Csv(SchoolName)}\"");
            csv.AppendLine($"\"Academic School Year\",\"{Csv(CurrentSchoolYear)}\"");
            csv.AppendLine($"\"Academic Semester\",\"{Csv(CurrentSemester)}\"");
            csv.AppendLine();
            csv.AppendLine("\"Student ID Number\",\"Full Legal Name\",\"Sex\",\"Degree Program\",\"Year Level\",\"Total Units Enrolled\",\"Matriculation Type\",\"Enrollment Status\"");
            foreach (var item in EnrolledMasterlist)
                csv.AppendLine($"\"{Csv(item.StudentIdNumber)}\",\"{Csv(item.Name)}\",\"{Csv(item.Gender)}\",\"{Csv(item.Program)}\",\"{Csv(item.YearLevel)}\",{item.TotalUnits},\"{Csv(item.EnrollmentType.ToUpperInvariant())}\",\"{Csv(item.EnrollmentStatus.ToUpperInvariant())}\"");
            var fileName = $"CHED_Masterlist_{CurrentSchoolYear.Replace(" ", "_")}_{CurrentSemester.Replace(" ", "_")}.csv";
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRecordSoAsync(int student_id, string so_number, string series_year, string program, string status, string? date_applied, string? date_issued, string? remarks)
    {
        _db.EnsureRegistrarSession();
        var number = (so_number ?? "").Trim();
        if (student_id <= 0 || string.IsNullOrEmpty(number))
        {
            TempData["FlashMessage"] = "Please provide student and S.O. number.";
            TempData["FlashType"] = "error";
            return RedirectToPage("/GovernmentCompliance", new { tab = "so" });
        }

        var applied = DateTime.TryParse(date_applied, out var appliedDate) ? appliedDate : DateTime.Today;
        var issued = DateTime.TryParse(date_issued, out var issuedDate) ? issuedDate : (DateTime?)null;
        _store.UpdateRoot(root =>
        {
            var records = CanonicalJsonStore.GetOrCreateArray(root, "ched_special_orders");
            records.Add(new JsonObject
            {
                ["id"] = NextId(records),
                ["student_id"] = student_id,
                ["so_number"] = number,
                ["series_year"] = (series_year ?? DateTime.Now.Year.ToString()).Trim(),
                ["program"] = (program ?? "BS Information Technology").Trim(),
                ["date_applied"] = applied.ToString("O"),
                ["date_issued"] = issued?.ToString("O"),
                ["status"] = (status ?? "Applied").Trim(),
                ["remarks"] = (remarks ?? "").Trim()
            });
        });
        await _db.LogActivityAsync(student_id, $"CHED Special Order recorded ({number}) by {_db.CurrentUserName}.");
        TempData["FlashMessage"] = "CHED Special Order recorded successfully.";
        TempData["FlashType"] = "success";
        return RedirectToPage("/GovernmentCompliance", new { tab = "so" });
    }

    public async Task<IActionResult> OnPostRecordNstpAsync(int student_id, string nstp_component, string serial_number, string? date_issued, string? remarks)
    {
        _db.EnsureRegistrarSession();
        var serial = (serial_number ?? "").Trim().ToUpperInvariant();
        if (student_id <= 0 || string.IsNullOrEmpty(serial))
        {
            TempData["FlashMessage"] = "Please provide student and serial number.";
            TempData["FlashType"] = "error";
            return RedirectToPage("/GovernmentCompliance", new { tab = "nstp" });
        }

        var issued = DateTime.TryParse(date_issued, out var issuedDate) ? issuedDate : DateTime.Today;
        _store.UpdateRoot(root =>
        {
            var records = CanonicalJsonStore.GetOrCreateArray(root, "nstp_serial_numbers");
            records.Add(new JsonObject
            {
                ["id"] = NextId(records),
                ["student_id"] = student_id,
                ["nstp_component"] = (nstp_component ?? "CWTS").Trim(),
                ["serial_number"] = serial,
                ["date_issued"] = issued.ToString("O"),
                ["remarks"] = (remarks ?? "").Trim()
            });
        });
        await _db.LogActivityAsync(student_id, $"NSTP Serial Number recorded ({serial}) by {_db.CurrentUserName}.");
        TempData["FlashMessage"] = "NSTP Serial Number recorded successfully.";
        TempData["FlashType"] = "success";
        return RedirectToPage("/GovernmentCompliance", new { tab = "nstp" });
    }

    private static List<JsonObject> ReadRows(JsonObject root, string collection) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();

    private static int NextId(JsonArray records) => records.OfType<JsonObject>().Select(item => IntValue(item, "id")).DefaultIfEmpty(0).Max() + 1;
    private static int IntValue(JsonObject? item, string property) => item?[property]?.GetValue<int>() ?? 0;
    private static string StringValue(JsonObject? item, string property, string fallback = "") => item?[property]?.GetValue<string>() ?? fallback;
    private static DateTime DateValue(JsonObject item, string property, DateTime fallback) => DateTime.TryParse(StringValue(item, property), out var value) ? value : fallback;
    private static DateTime? NullableDateValue(JsonObject item, string property) => DateTime.TryParse(StringValue(item, property), out var value) ? value : null;
    private static string Csv(string value) => value.Replace("\"", "\"\"");
}
