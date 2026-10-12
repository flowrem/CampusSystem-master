using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RegistrarMain.Services;

namespace RegistrarMain.Pages;

/// <summary>
/// Shows which department apps are reachable, what is in the shared datastore, and lets staff try the real
/// department endpoints. The probe runs on the server, so browsers do not need cross-origin access.
/// </summary>
public class IntegrationHubModel(DatabaseService db, CanonicalJsonStore store, RegistrarApiStore api, IConfiguration configuration, IHttpClientFactory httpFactory) : PageModel
{
    // What each department exposes. "Self" actions are answered from the shared datastore by Registrar's own API store.
    public static readonly IReadOnlyDictionary<string, string[]> Actions = new Dictionary<string, string[]>
    {
        ["registrar"] = ["courses", "registrations", "transcript", "records", "verifications"],
        ["finance"] = ["assessment", "clearance"],
        ["guidance"] = ["students"],
    };

    public string FilePath => store.GetPath();
    public int SchemaVersion { get; private set; }
    public List<CollectionCount> Collections { get; private set; } = [];
    public List<ActivityItem> Activity { get; private set; } = [];
    public List<StudentOption> Students { get; private set; } = [];
    public List<DepartmentLink> Departments => ConfiguredDepartments().ToList();

    public void OnGet()
    {
        db.EnsureRegistrarSession();
        var root = store.ReadRoot();
        SchemaVersion = root["system"]?["schema_version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

        Collections = (root["unified_seed_data"] as JsonObject ?? new JsonObject())
            .Where(pair => pair.Value is JsonArray)
            .Select(pair => new CollectionCount(pair.Key, ((JsonArray)pair.Value!).Count))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        Students = Rows(root, "users").Where(u => Str(u, "role") == "student")
            .OrderBy(u => Str(u, "name"))
            .Select(u => new StudentOption(Int(u, "id"), Str(u, "student_id_number"), Str(u, "name")))
            .ToList();

        var recent = Rows(root, "activity_log").Select(a => new ActivityItem(Str(a, "created_at"), "activity", Str(a, "user_name"), Str(a, "message")))
            .Concat(Rows(root, "audit_events").Select(a => new ActivityItem(Str(a, "timestamp"), "audit", Str(a, "actor_id"), $"{Str(a, "action")} {Str(a, "entity")} #{Str(a, "entity_id")}: {Str(a, "before_state")} → {Str(a, "after_state")}")));
        Activity = recent.OrderByDescending(a => a.At, StringComparer.Ordinal).Take(12).ToList();
    }

    /// <summary>Pings every configured department app and reports whether it answered.</summary>
    public async Task<IActionResult> OnGetPingAsync(CancellationToken cancellationToken)
    {
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        var results = await Task.WhenAll(ConfiguredDepartments().Select(async department =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(department.Url, cancellationToken);
                return new { name = department.Name, url = department.Url, up = (int)response.StatusCode < 500, status = (int)response.StatusCode, ms = watch.ElapsedMilliseconds };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new { name = department.Name, url = department.Url, up = false, status = 0, ms = watch.ElapsedMilliseconds };
            }
        }));
        return new JsonResult(results);
    }

    /// <summary>Runs one of the whitelisted actions and returns the department's JSON.</summary>
    public async Task<IActionResult> OnGetProbeAsync(string system, string action, string? param, CancellationToken cancellationToken)
    {
        if (!Actions.TryGetValue(system ?? "", out var allowed) || !allowed.Contains(action ?? "", StringComparer.Ordinal))
            return BadRequest(new { error = "Unknown system or action." });

        param = (param ?? "").Trim();
        if (system == "registrar")
        {
            if (action == "courses") return new JsonResult(api.GetCourses(param));
            if (!int.TryParse(param, out var studentId) || !api.StudentExists(studentId))
                return BadRequest(new { error = "Enter the numeric id of a student (users.id)." });
            return new JsonResult(action switch
            {
                "registrations" => (object)api.GetRegistrations(studentId),
                "transcript" => api.GetTranscript(studentId),
                "records" => api.GetRecordsRequests(studentId),
                _ => api.GetVerifications(studentId),
            });
        }

        var baseUrl = Department(system!)?.Url;
        if (baseUrl is null) return BadRequest(new { error = $"{system} has no URL in CampusSystem configuration." });
        var path = (system, action) switch
        {
            ("finance", "assessment") => $"/api/finance/assessment?studentNumber={Uri.EscapeDataString(param)}",
            ("finance", "clearance") => $"/api/finance/clearance?studentNumber={Uri.EscapeDataString(param)}",
            _ => "/api/guidance/students",
        };

        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        if (configuration["CampusSystem:ServiceKey"] is { Length: > 0 } key) client.DefaultRequestHeaders.Add(CampusSystem.Data.Auth.CampusAuth.ServiceKeyHeader, key);
        try
        {
            using var response = await client.GetAsync(baseUrl.TrimEnd('/') + path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Content(body.Length == 0 ? "{}" : body, "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(502, new { error = $"{system} did not answer at {baseUrl}. Is it running?" });
        }
    }

    private DepartmentLink? Department(string name) =>
        ConfiguredDepartments().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<DepartmentLink> ConfiguredDepartments()
    {
        foreach (var (name, key) in new[]
                 {
                     ("student", "StudentPortalUrl"), ("faculty", "FacultyPortalUrl"), ("finance", "FinanceUrl"),
                     ("guidance", "GuidanceUrl"), ("library", "LibraryUrl"), ("testing", "TestingUrl"),
                 })
        {
            var url = configuration[$"CampusSystem:{key}"];
            if (!string.IsNullOrWhiteSpace(url)) yield return new DepartmentLink(name, url);
        }
    }

    public sealed record DepartmentLink(string Name, string Url);
    public sealed record CollectionCount(string Name, int Count);
    public sealed record ActivityItem(string At, string Kind, string Who, string Message);
    public sealed record StudentOption(int Id, string Number, string Name);

    private static IEnumerable<JsonObject> Rows(JsonObject root, string name) =>
        (root["unified_seed_data"]?[name] as JsonArray ?? new JsonArray()).OfType<JsonObject>();
    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static int Int(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
}
