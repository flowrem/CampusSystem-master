using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using TestingMain.Services;

namespace TestingMain.Tests;

public sealed class JsonCollectionBrowserTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "users": [
          { "id": 1, "name": "Ana", "active": true, "age": 20, "tags": ["a"], "note": null },
          { "id": 2, "name": "Ben", "extra": "only here" }
        ],
        "settings": [ { "key": "current_semester", "value": "1st Semester" } ],
        "empty_things": [],
        "system_note": "not a collection"
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TestingBrowserTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CanonicalJsonStore _json;
    private readonly JsonCollectionBrowser _browser;

    public JsonCollectionBrowserTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(_path, Fixture);
        _json = new CanonicalJsonStore(_path);
        _browser = new JsonCollectionBrowser(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private JsonObject User(int id) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), "users").OfType<JsonObject>().First(u => u["id"]!.GetValue<int>() == id);

    [Fact]
    public void Lists_only_array_collections_with_counts()
    {
        Assert.Equal(["empty_things:0", "settings:1", "users:2"], _browser.ListCollections().Select(c => $"{c.Name}:{c.Count}"));
        Assert.Equal(1, _browser.SchemaVersion);
        Assert.Equal(_path, _browser.FilePath);
    }

    [Fact]
    public void Read_unions_columns_and_flags_rows_without_ids()
    {
        var users = _browser.Read("users");

        Assert.Equal(["id", "name", "active", "age", "tags", "note", "extra"], users.Columns);
        Assert.Equal(["1", "Ana", "true", "20", "[\"a\"]", "", ""], users.Rows[0]);
        Assert.Equal("only here", users.Rows[1][6]);
        Assert.Equal([1, 2], users.Ids);
        Assert.Equal([(int?)null], _browser.Read("settings").Ids);
    }

    [Fact]
    public void Read_rejects_unknown_or_blank_collection()
    {
        Assert.Throws<BrowserException>(() => _browser.Read("nope"));
        Assert.Throws<BrowserException>(() => _browser.Read("system_note"));
        Assert.Throws<BrowserException>(() => _browser.Read(" "));
    }

    [Fact]
    public void Add_assigns_next_id_or_honours_a_free_one()
    {
        Assert.Equal(3, _browser.Add("users", """{ "name": "Cy" }"""));
        Assert.Equal(10, _browser.Add("users", """{ "id": 10, "name": "Di" }"""));
        Assert.Equal(1, _browser.Add("empty_things", "{}"));
        Assert.Equal("Cy", User(3)["name"]!.GetValue<string>());
    }

    [Fact]
    public void Add_rejects_bad_input_without_touching_the_file()
    {
        var before = File.ReadAllText(_path);

        Assert.Throws<BrowserException>(() => _browser.Add("users", "not json"));
        Assert.Throws<BrowserException>(() => _browser.Add("users", "[1,2]"));
        Assert.Throws<BrowserException>(() => _browser.Add("users", """{ "id": 1 }"""));
        Assert.Throws<BrowserException>(() => _browser.Add("users", """{ "id": "x" }"""));
        Assert.Throws<BrowserException>(() => _browser.Add("missing", "{}"));

        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public void Update_keeps_each_fields_type_and_skips_unknown_keys_and_the_id()
    {
        _browser.Update("users", 1, new Dictionary<string, string>
        {
            ["id"] = "99", ["name"] = "Anna", ["active"] = "false", ["age"] = "21", ["tags"] = "ignored", ["nonexistent"] = "x"
        });

        var row = User(1);
        Assert.Equal(1, row["id"]!.GetValue<int>());
        Assert.Equal("Anna", row["name"]!.GetValue<string>());
        Assert.False(row["active"]!.GetValue<bool>());
        Assert.Equal(21m, row["age"]!.GetValue<decimal>());
        Assert.Equal("a", row["tags"]![0]!.GetValue<string>());
        Assert.False(row.ContainsKey("nonexistent"));
    }

    [Fact]
    public void Update_rejects_values_of_the_wrong_type_and_changes_nothing()
    {
        var before = File.ReadAllText(_path);

        Assert.Throws<BrowserException>(() => _browser.Update("users", 1, new Dictionary<string, string> { ["name"] = "Zed", ["age"] = "old" }));
        Assert.Throws<BrowserException>(() => _browser.Update("users", 1, new Dictionary<string, string> { ["active"] = "maybe" }));
        Assert.Throws<BrowserException>(() => _browser.Update("users", 404, new Dictionary<string, string>()));

        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public void Delete_removes_only_the_matching_row()
    {
        _browser.Delete("users", 1);

        Assert.Equal([2], _browser.Read("users").Ids);
        Assert.Throws<BrowserException>(() => _browser.Delete("users", 1));
    }

    [Fact]
    public void Repository_data_is_browsable()
    {
        var browser = new JsonCollectionBrowser(new CanonicalJsonStore(CanonicalJsonStore.ResolvePath()));

        var collections = browser.ListCollections();
        Assert.Contains(collections, c => c.Name == "users" && c.Count > 0);
        Assert.NotEmpty(browser.Read("users").Rows);
    }
}
