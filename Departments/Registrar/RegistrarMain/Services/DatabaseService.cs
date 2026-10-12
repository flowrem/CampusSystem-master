using System.Security.Claims;
using System.Text.Json.Nodes;
using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;

namespace RegistrarMain.Services;

/// <summary>
/// Registrar session helpers, settings and activity log on top of the unified JSON store.
/// </summary>
public class DatabaseService
{
    private readonly IHttpContextAccessor _ctx;
    private readonly CanonicalJsonStore _jsonStore;

    public DatabaseService(IHttpContextAccessor ctx, CanonicalJsonStore jsonStore)
    {
        _ctx = ctx;
        _jsonStore = jsonStore;
    }

    // ─── JSON Store Sync ─────────────────────────────────────────────────────

    public Task RegisterStudentInJsonStoreAsync(
        int userId,
        string studentIdNumber,
        string name,
        string email,
        string program,
        string yearLevel,
        string gender,
        string contactNumber,
        string address,
        string passwordHash)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var users = CanonicalJsonStore.GetOrCreateArray(root, "users");
            var existing = users.OfType<JsonObject>().FirstOrDefault(user =>
                string.Equals(user["student_id_number"]?.ToString(), studentIdNumber, StringComparison.Ordinal));

            int id;
            if (existing != null)
            {
                id = existing["id"]!.GetValue<int>();
            }
            else
            {
                if (userId > 0 && users.OfType<JsonObject>().Any(user => user["id"]?.GetValue<int>() == userId))
                    throw new InvalidOperationException($"User id {userId} already belongs to another student.");

                id = userId > 0 ? userId : CanonicalJsonStore.NextId(users);
                users.Add(new JsonObject
                {
                    ["id"] = id,
                    ["student_id_number"] = studentIdNumber,
                    ["name"] = name,
                    ["email"] = email,
                    ["role"] = "student",
                    ["status"] = "active",
                    ["password_hash"] = passwordHash,
                    ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                });
            }

            var profiles = CanonicalJsonStore.GetOrCreateArray(root, "student_profiles");
            if (profiles.OfType<JsonObject>().Any(profile => profile["user_id"]?.GetValue<int>() == id)) return;

            profiles.Add(new JsonObject
            {
                ["user_id"] = id,
                ["program"] = program,
                ["year_level"] = yearLevel,
                ["curriculum_year"] = "2024-2028",
                ["average_grade"] = 1.50,
                ["academic_status"] = "Good Standing",
                ["enrollment_status"] = "Regular / Enrolled",
                ["completed_units"] = 0,
                ["units_remaining"] = 144,
                ["guidance_clearance_status"] = "Pending",
                ["guidance_notes"] = "New applicant registered via Registrar Office",
                ["contact_number"] = contactNumber,
                ["address"] = address,
                ["gender"] = gender
            });
        });
        return Task.CompletedTask;
    }

    // ─── Settings ────────────────────────────────────────────────────────────

    public async Task<string> GetSettingAsync(string key, string defaultValue = "")
    {
        var settings = CanonicalJsonStore.GetArray(_jsonStore.ReadRoot(), "settings");
        foreach (var setting in settings.OfType<JsonObject>())
        {
            if (string.Equals(setting["key"]?.GetValue<string>(), key, StringComparison.Ordinal))
                return setting["value"]?.GetValue<string>() ?? defaultValue;
        }
        return defaultValue;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var settings = CanonicalJsonStore.GetOrCreateArray(root, "settings");
            var setting = settings.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["key"]?.GetValue<string>(), key, StringComparison.Ordinal));
            if (setting == null) settings.Add(new JsonObject { ["key"] = key, ["value"] = value });
            else setting["value"] = value;
        });
    }

    // ─── Activity Log ────────────────────────────────────────────────────────

    public async Task LogActivityAsync(int? userId, string message)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var activities = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
            var nextId = activities.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            activities.Add(new JsonObject
            {
                ["id"] = nextId,
                ["user_id"] = userId,
                ["user_name"] = CurrentUserName,
                ["message"] = message,
                ["created_at"] = DateTime.UtcNow.ToString("O")
            });
        });
    }

    // ─── Signed-in user ──────────────────────────────────────────────────────

    private ClaimsPrincipal? Principal => _ctx.HttpContext?.User;

    public int CurrentUserId => Principal?.GetUserId() ?? 0;

    public string CurrentUserName => Principal?.Identity?.Name ?? "Unknown";

    public string CurrentUserRole => Principal is { Identity.IsAuthenticated: true } p && p.GetRole() is { Length: > 0 } role ? role : "registrar";

    /// <summary>Kept so existing pages compile. Sign-in is real now (CampusAuth), so there is nothing to establish here.</summary>
    public void EnsureRegistrarSession() { }

    public void SwitchToRegistrar() { }
}
