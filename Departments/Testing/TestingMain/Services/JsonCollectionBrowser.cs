using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace TestingMain.Services;

/// <summary>Raised when a browse or edit request is invalid. The message is safe to show.</summary>
public sealed class BrowserException(string message) : Exception(message);

/// <summary>
/// A generic viewer and editor for the collections under <c>unified_seed_data</c>. Every write goes through
/// <see cref="CanonicalJsonStore.UpdateRoot"/>, so edits are safe next to the running department apps.
/// </summary>
public sealed class JsonCollectionBrowser(CanonicalJsonStore store)
{
    public string FilePath => store.GetPath();

    public int SchemaVersion => store.ReadRoot()["system"]?["schema_version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    public IReadOnlyList<CollectionInfo> ListCollections()
    {
        var data = store.ReadRoot()["unified_seed_data"] as JsonObject ?? new JsonObject();
        return data
            .Where(pair => pair.Value is JsonArray)
            .Select(pair => new CollectionInfo(pair.Key, ((JsonArray)pair.Value!).Count))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Rows with the union of their keys, in first-seen order. An unknown collection raises <see cref="BrowserException"/>.</summary>
    public CollectionView Read(string collection)
    {
        var rows = Collection(store.ReadRoot(), collection).OfType<JsonObject>().ToList();
        var columns = new List<string>();
        foreach (var row in rows)
            foreach (var key in row.Select(p => p.Key).Where(k => !columns.Contains(k)))
                columns.Add(key);

        return new CollectionView(collection, columns, rows.Select(r => columns.Select(c => Display(r[c])).ToList()).ToList(),
            rows.Select(r => r["id"] is JsonValue v && v.TryGetValue<int>(out var id) ? id : (int?)null).ToList());
    }

    /// <summary>Adds the object, giving it the next id when it has none. Returns the id.</summary>
    public int Add(string collection, string json)
    {
        var id = 0;
        store.UpdateRoot(root =>
        {
            var rows = Collection(root, collection);
            JsonObject row;
            try { row = JsonNode.Parse(json) as JsonObject ?? throw new BrowserException("Enter one JSON object."); }
            catch (System.Text.Json.JsonException ex) { throw new BrowserException($"That is not valid JSON: {ex.Message}"); }

            if (row["id"] is null) row["id"] = CanonicalJsonStore.NextId(rows);
            else if (row["id"] is not JsonValue v || !v.TryGetValue<int>(out var given) || rows.OfType<JsonObject>().Any(r => IdOf(r) == given))
                throw new BrowserException("The id must be a whole number that no other row uses.");

            id = IdOf(row);
            rows.Add(row);
        });
        return id;
    }

    /// <summary>Updates the scalar fields of one row. Each value keeps the type the field already has.</summary>
    public void Update(string collection, int id, IReadOnlyDictionary<string, string> values)
    {
        store.UpdateRoot(root =>
        {
            var row = Find(Collection(root, collection), id);
            foreach (var (key, text) in values)
            {
                if (key == "id" || !row.ContainsKey(key)) continue;
                row[key] = Convert(key, row[key], text);
            }
        });
    }

    public void Delete(string collection, int id)
    {
        store.UpdateRoot(root =>
        {
            var rows = Collection(root, collection);
            rows.Remove(Find(rows, id));
        });
    }

    private static JsonNode? Convert(string key, JsonNode? current, string text)
    {
        switch (current)
        {
            case JsonValue v when v.TryGetValue<bool>(out _):
                return bool.TryParse(text, out var b) ? JsonValue.Create(b) : throw Bad(key, "true or false");
            case JsonValue v when v.TryGetValue<string>(out _):
                return JsonValue.Create(text);
            case JsonValue:
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : throw Bad(key, "a number");
            case null:
                return string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);
            default:
                return current.DeepClone(); // objects and arrays are left alone
        }
    }

    private static BrowserException Bad(string field, string expected) => new($"{field} must be {expected}.");

    private static string RequireName(string collection) =>
        string.IsNullOrWhiteSpace(collection) ? throw new BrowserException("Choose a collection.") : collection;

    private static JsonArray Collection(JsonObject root, string name) =>
        (root["unified_seed_data"] as JsonObject)?[RequireName(name)] as JsonArray
        ?? throw new BrowserException($"There is no collection named {name}.");

    private static JsonObject Find(JsonArray rows, int id) =>
        rows.OfType<JsonObject>().FirstOrDefault(r => IdOf(r) == id) ?? throw new BrowserException($"No row has id {id}.");

    private static int IdOf(JsonObject row) =>
        row["id"] is JsonValue v && v.TryGetValue<int>(out var id) ? id : 0;

    private static string Display(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString()
    };
}

public sealed record CollectionInfo(string Name, int Count);

/// <summary>Rows as display strings. <see cref="Ids"/> is null for a row with no whole-number id, which cannot be edited here.</summary>
public sealed record CollectionView(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<int?> Ids);
