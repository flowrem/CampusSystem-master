using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CampusSystem.Data.Json;

public sealed class CanonicalJsonStore
{
    public const string FileName = "campus_system_unified_data_schema.json";
    public const string SeedFileName = "campus_system_seed_data.json";
    public const int CurrentSchemaVersion = 2;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Action<string, Exception?>? _log;
    private readonly string? _path;

    public CanonicalJsonStore(Action<string, Exception?>? log = null)
    {
        _log = log;
    }

    // Pins the store to one file instead of locating the repository root. Used by tests.
    public CanonicalJsonStore(string path, Action<string, Exception?>? log = null)
    {
        _path = Path.GetFullPath(path);
        _log = log;
    }

    public string GetPath()
        => _path ?? ResolvePath();

    public static string ResolvePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CAMPUS_SYSTEM_JSON_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullPath)) return fullPath;
            throw new FileNotFoundException("Configured canonical JSON datastore was not found.", fullPath);
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, FileName);
                var isRepositoryRoot = Directory.Exists(Path.Combine(directory.FullName, "Departments"));
                if (isRepositoryRoot && File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("The canonical root JSON datastore could not be located.", FileName);
    }

    public JsonObject ReadRoot()
    {
        var path = GetPath();
        return WithFileLock(path, () => ReadRootUnlocked(path));
    }

    // Every department app runs in its own process, so writes are serialized with a
    // named OS mutex rather than an in-process lock. Readers take the same mutex so a
    // read never holds the file open while another process replaces it.
    public void UpdateRoot(Action<JsonObject> update)
    {
        var path = GetPath();
        WithFileLock(path, () =>
        {
            var root = ReadRootUnlocked(path);
            update(root);
            StampSchemaVersion(root);
            WriteRootUnlocked(path, root);
            return true;
        });
    }

    // Restores the live datastore from the pristine seed file kept beside it.
    // The current file is copied to BackupFolder first.
    public string ResetToSeed()
    {
        var path = GetPath();
        var seedPath = Path.Combine(Path.GetDirectoryName(path)!, SeedFileName);
        if (!File.Exists(seedPath))
            throw new FileNotFoundException("The seed JSON datastore could not be located.", seedPath);

        return WithFileLock(path, () =>
        {
            var backupPath = BackupUnlocked(path);
            var seed = JsonNode.Parse(File.ReadAllText(seedPath)) as JsonObject
                ?? throw new InvalidDataException("The seed JSON datastore root is not an object.");
            WriteRootUnlocked(path, seed);
            return backupPath;
        });
    }

    public string Backup()
    {
        var path = GetPath();
        return WithFileLock(path, () => BackupUnlocked(path));
    }

    public static JsonArray GetArray(JsonObject root, string collection)
    {
        var seed = root["unified_seed_data"] as JsonObject
            ?? throw new InvalidDataException("The canonical JSON datastore has no unified_seed_data object.");
        return seed[collection] as JsonArray ?? throw new InvalidDataException($"The canonical JSON datastore has no '{collection}' collection.");
    }

    public static JsonArray GetOrCreateArray(JsonObject root, string collection)
    {
        var seed = root["unified_seed_data"] as JsonObject
            ?? throw new InvalidDataException("The canonical JSON datastore has no unified_seed_data object.");
        return seed[collection] as JsonArray ?? (seed[collection] = new JsonArray()).AsArray();
    }

    // Only safe inside UpdateRoot: the id is unique because the whole read-modify-write
    // runs under the cross-process lock.
    public static int NextId(JsonObject root, string collection)
        => NextId(GetOrCreateArray(root, collection));

    public static int NextId(JsonArray rows)
        => rows.OfType<JsonObject>()
            .Select(row => row["id"] is JsonValue value && value.TryGetValue<int>(out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private JsonObject ReadRootUnlocked(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("The canonical JSON datastore root is not an object.");
            EnsureSupportedSchemaVersion(root);
            return root;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Failed to read canonical JSON datastore at {path}.", ex);
            throw;
        }
    }

    private void WriteRootUnlocked(string path, JsonObject root)
    {
        var tempPath = path + ".tmp";
        try
        {
            var json = root.ToJsonString(WriteOptions);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(tempPath, path, true);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Failed to write canonical JSON datastore at {path}.", ex);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static string BackupUnlocked(string path)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(path)!, "BackupFolder", "json");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
        File.Copy(path, backupPath, false);
        return backupPath;
    }

    // An app built against an older schema must not rewrite a file a newer app has upgraded,
    // or it would silently drop the fields it does not know about.
    private static void EnsureSupportedSchemaVersion(JsonObject root)
    {
        if (root["system"]?["schema_version"] is not JsonValue value) return;
        if (!value.TryGetValue<int>(out var version))
            throw new InvalidDataException("The canonical JSON datastore schema_version is not an integer.");
        if (version > CurrentSchemaVersion)
            throw new InvalidDataException(
                $"The canonical JSON datastore is schema_version {version}, but this app supports up to {CurrentSchemaVersion}. Rebuild the app against the current CampusSystem.Data.");
    }

    private static void StampSchemaVersion(JsonObject root)
    {
        var system = root["system"] as JsonObject ?? (root["system"] = new JsonObject()).AsObject();
        system["schema_version"] ??= CurrentSchemaVersion;
    }

    private T WithFileLock<T>(string path, Func<T> action)
    {
        using var mutex = new Mutex(false, MutexName(path));
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(LockTimeout);
            }
            catch (AbandonedMutexException ex)
            {
                // Another process crashed while holding the lock; ownership passes to us.
                _log?.Invoke("Recovered an abandoned canonical JSON datastore lock.", ex);
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException($"Timed out waiting for the canonical JSON datastore lock on {path}.");

            return action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    // One mutex per datastore file, so separate checkouts of the repo do not block each other.
    private static string MutexName(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return $"Local\\CampusSystemJsonStore_{Convert.ToHexString(hash, 0, 8)}";
    }
}
