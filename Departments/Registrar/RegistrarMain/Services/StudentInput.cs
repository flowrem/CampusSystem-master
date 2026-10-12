using System.Text.Json.Nodes;

namespace RegistrarMain.Services;

public sealed record StudentCreateInput(string StudentIdNumber, string Name, string Email, string Program, string YearLevel, string Gender, string? ContactNumber);

public static class StudentInput
{
    public static readonly string[] Programs =
    [
        "BS Information Technology",
        "BS Computer Science",
        "BS Business Administration",
        "BS Accountancy",
        "BS Psychology",
        "BS Tourism Management",
        "BS Hospitality Management",
        "BS Criminology",
        "BS Architecture",
        "BS Nursing"
    ];

    public static readonly string[] YearLevels = ["1st Year", "2nd Year", "3rd Year", "4th Year"];

    public static string? Validate(string? studentIdNumber, string? name, string? email, string? program, string? yearLevel, string? gender, string? contactNumber, out StudentCreateInput input)
    {
        input = new StudentCreateInput((studentIdNumber ?? "").Trim(), (name ?? "").Trim(), (email ?? "").Trim(), (program ?? "").Trim(), (yearLevel ?? "").Trim(), (gender ?? "").Trim(), string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber.Trim());
        if (input.Name.Length == 0) return "Name is required.";
        if (input.Email.Length == 0 || !input.Email.Contains('@')) return "A valid email address is required.";
        if (!Programs.Contains(input.Program, StringComparer.Ordinal)) return "Please select a valid degree program.";
        if (!YearLevels.Contains(input.YearLevel, StringComparer.Ordinal)) return "Please select a valid year level.";
        return null;
    }

    public static bool HasMatch(IEnumerable<string?> storedValues, string? value) =>
        !string.IsNullOrWhiteSpace(value) && storedValues.Any(stored =>
            string.Equals(stored, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ResolveStudentId(string? studentIdNumber, int userId) =>
        string.IsNullOrWhiteSpace(studentIdNumber) ? $"2026-{userId:D5}" : studentIdNumber.Trim();

    public static JsonObject FindUserByStudentId(IEnumerable<JsonObject> users, string? studentIdNumber) =>
        users.FirstOrDefault(user => string.Equals(
            user["student_id_number"]?.GetValue<string>(),
            (studentIdNumber ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Student account was not found.");
}
