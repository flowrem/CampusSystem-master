namespace RegistrarMain.Services;

/// <summary>
/// Provides academic term settings — cached per request.
/// </summary>
public class SettingsService
{
    private readonly DatabaseService _db;
    private string? _schoolYear;
    private string? _semester;
    private string? _encodingOpen;
    private string? _encodingDeadline;

    public SettingsService(DatabaseService db) => _db = db;

    public async Task<string> GetSchoolYearAsync()
        => _schoolYear ??= await _db.GetSettingAsync("current_school_year", "2025-2026");

    public async Task<string> GetSemesterAsync()
        => _semester ??= await _db.GetSettingAsync("current_semester", "1st Semester");

    public async Task<bool> IsEncodingOpenAsync()
        => (_encodingOpen ??= await _db.GetSettingAsync("grade_encoding_open", "1")) == "1";

    public async Task<string> GetEncodingDeadlineAsync()
        => _encodingDeadline ??= await _db.GetSettingAsync("grade_encoding_deadline", "2025-10-30");
}
