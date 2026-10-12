using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSystem.Data.Tests;

public sealed class CanonicalJsonStoreTests : IDisposable
{
    // Every collection named in Shared/CampusSystem.Data/Json/README.md. Keep the two in step.
    public static readonly string[] OwnedCollections =
    [
        "users", "student_profiles", "subjects", "class_offerings", "enrollments", "enrolled_subjects",
        "certificate_of_registrations", "document_credentials", "transcript_requests", "add_drop_requests",
        "overload_waiver_requests", "course_shifting_requests", "completion_revision_requests",
        "transferee_credited_subjects", "nstp_serial_numbers", "ched_special_orders",
        "tuition_fee_matrix", "fee_assessments", "installment_schedules", "student_payments",
        "scholarships", "scholarship_applications", "fee_adjustments",
        "grades", "attendance_logs", "assignments", "assignment_submissions",
        "guidance_requests", "student_guidance_tickets", "counseling_case_notes", "guidance_appointments",
        "library_books", "book_loans", "library_fines", "library_reservations",
        "settings", "activity_log", "audit_events", "refresh_tokens", "student_clearance",
    ];

    private readonly string _directory;
    private readonly string _path;

    public CanonicalJsonStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CampusSystemJsonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        WriteFile(_path, """{ "system": { "schema_version": 1 }, "unified_seed_data": { "users": [ { "id": 3 }, { "id": 7 } ] } }""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public void ReadRoot_ReturnsCopy_ThatDoesNotWriteBack()
    {
        var store = new CanonicalJsonStore(_path);

        var root = store.ReadRoot();
        CanonicalJsonStore.GetArray(root, "users").Clear();

        Assert.Equal(2, CanonicalJsonStore.GetArray(store.ReadRoot(), "users").Count);
    }

    [Fact]
    public void UpdateRoot_PersistsChange()
    {
        var store = new CanonicalJsonStore(_path);

        store.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "users").Add(new JsonObject { ["id"] = 8 }));

        Assert.Equal(3, CanonicalJsonStore.GetArray(store.ReadRoot(), "users").Count);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void UpdateRoot_StampsSchemaVersion_WhenMissing()
    {
        WriteFile(_path, """{ "unified_seed_data": {} }""");
        var store = new CanonicalJsonStore(_path);

        store.UpdateRoot(_ => { });

        Assert.Equal(CanonicalJsonStore.CurrentSchemaVersion, store.ReadRoot()["system"]!["schema_version"]!.GetValue<int>());
    }

    [Fact]
    public void UpdateRoot_LeavesFileUntouched_WhenCallbackThrows()
    {
        var store = new CanonicalJsonStore(_path);
        var before = File.ReadAllText(_path);

        Assert.Throws<InvalidOperationException>(() => store.UpdateRoot(root =>
        {
            CanonicalJsonStore.GetArray(root, "users").Clear();
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public void Store_RejectsNewerSchemaVersion_WithoutTouchingFile()
    {
        var newer = $$"""{ "system": { "schema_version": {{CanonicalJsonStore.CurrentSchemaVersion + 1}} }, "unified_seed_data": {} }""";
        WriteFile(_path, newer);
        var store = new CanonicalJsonStore(_path);

        Assert.Throws<InvalidDataException>(() => store.ReadRoot());
        Assert.Throws<InvalidDataException>(() => store.UpdateRoot(_ => { }));
        Assert.Equal(newer, File.ReadAllText(_path));
    }

    [Fact]
    public void NextId_IsOneAboveHighestId()
    {
        var root = new CanonicalJsonStore(_path).ReadRoot();

        Assert.Equal(8, CanonicalJsonStore.NextId(root, "users"));
        Assert.Equal(1, CanonicalJsonStore.NextId(root, "library_fines"));
        Assert.Equal(1, CanonicalJsonStore.NextId(new JsonArray(new JsonObject { ["key"] = "no id" })));
    }

    [Fact]
    public void GetArray_Throws_ForMissingCollection_AndGetOrCreateArray_AddsIt()
    {
        var root = new CanonicalJsonStore(_path).ReadRoot();

        Assert.Throws<InvalidDataException>(() => CanonicalJsonStore.GetArray(root, "grades"));
        CanonicalJsonStore.GetOrCreateArray(root, "grades").Add(new JsonObject());
        Assert.Single(CanonicalJsonStore.GetArray(root, "grades"));
    }

    // Each store instance opens its own handle on the named mutex, the same as separate
    // department processes do. A lost update or a duplicate id means the lock failed.
    [Fact]
    public void ConcurrentWriters_DoNotLoseUpdates_OrDuplicateIds()
    {
        const int writers = 8;
        const int insertsPerWriter = 25;

        Parallel.For(0, writers, new ParallelOptions { MaxDegreeOfParallelism = writers }, _ =>
        {
            var store = new CanonicalJsonStore(_path);
            for (var i = 0; i < insertsPerWriter; i++)
            {
                store.UpdateRoot(root =>
                {
                    var users = CanonicalJsonStore.GetArray(root, "users");
                    users.Add(new JsonObject { ["id"] = CanonicalJsonStore.NextId(users) });
                });
            }
        });

        var ids = CanonicalJsonStore.GetArray(new CanonicalJsonStore(_path).ReadRoot(), "users")
            .Select(user => user!["id"]!.GetValue<int>())
            .ToList();
        Assert.Equal(2 + writers * insertsPerWriter, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ResetToSeed_BacksUpLiveFile_AndRestoresSeed()
    {
        var seed = """{ "system": { "schema_version": 1 }, "unified_seed_data": { "users": [] } }""";
        WriteFile(Path.Combine(_directory, CanonicalJsonStore.SeedFileName), seed);
        var live = File.ReadAllText(_path);
        var store = new CanonicalJsonStore(_path);

        var backupPath = store.ResetToSeed();

        Assert.Equal(live, File.ReadAllText(backupPath));
        Assert.StartsWith(Path.Combine(_directory, "BackupFolder", "json"), backupPath);
        Assert.Empty(CanonicalJsonStore.GetArray(store.ReadRoot(), "users"));
    }

    [Fact]
    public void Store_ResolvesFromDependencyInjection()
    {
        using var services = new ServiceCollection().AddSingleton<CanonicalJsonStore>().BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<CanonicalJsonStore>());
    }

    [Theory]
    [InlineData(CanonicalJsonStore.FileName)]
    [InlineData(CanonicalJsonStore.SeedFileName)]
    public void RepositoryDataFiles_LoadAndContainEveryOwnedCollection(string fileName)
    {
        var path = Path.Combine(Path.GetDirectoryName(CanonicalJsonStore.ResolvePath())!, fileName);
        var root = new CanonicalJsonStore(path).ReadRoot();

        var missing = OwnedCollections.Where(name => root["unified_seed_data"]?[name] is not JsonArray).ToList();
        Assert.Empty(missing);
    }

    private static void WriteFile(string path, string json) => File.WriteAllText(path, json);
}
