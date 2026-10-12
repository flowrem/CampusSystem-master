using System.Text.Json.Nodes;
using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;

namespace CampusSystem.Data.Tests;

public sealed class AuthStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AuthStoreTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CanonicalJsonStore _json;
    private readonly AuthStore _auth;

    public AuthStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        var users = new JsonArray
        {
            User(1, "FIN-1", "Roberto Gomez", "finance", "roberto@example.edu", "Correct#1"),
            User(5, "2023-01045", "Alyssa Mendoza", "student", "alyssa@example.edu", "Student#99"),
            User(6, "2023-00000", "No Password", "student", "none@example.edu", null),
            User(7, "2020-00001", "Inactive Student", "student", "gone@example.edu", "Student#99", "inactive"),
        };
        File.WriteAllText(_path, new JsonObject
        {
            ["system"] = new JsonObject { ["schema_version"] = 1 },
            ["unified_seed_data"] = new JsonObject { ["users"] = users }
        }.ToJsonString());
        _json = new CanonicalJsonStore(_path);
        _auth = new AuthStore(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private static JsonObject User(int id, string number, string name, string role, string email, string? password, string status = "active") => new()
    {
        ["id"] = id, ["student_id_number"] = number, ["name"] = name, ["role"] = role, ["email"] = email, ["status"] = status,
        ["password_hash"] = password is null ? null : AuthStore.Hash(password)
    };

    private JsonObject Row(int id) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), "users").OfType<JsonObject>().First(u => u["id"]!.GetValue<int>() == id);

    [Fact]
    public void Login_AcceptsIdNumberOrEmail_AndRecordsTheLogin()
    {
        var byNumber = _auth.Login("FIN-1", "Correct#1");
        var byEmail = _auth.Login("  ROBERTO@example.edu ", "Correct#1");

        Assert.True(byNumber.Succeeded);
        Assert.Equal(("Roberto Gomez", "finance", 1), (byNumber.User!.Name, byNumber.User.Role, byNumber.User.Id));
        Assert.True(byEmail.Succeeded);
        Assert.NotNull(Row(1)["last_login"]);
    }

    [Theory]
    [InlineData("FIN-1", "wrong")]
    [InlineData("FIN-1", "")]
    [InlineData("nobody", "Correct#1")]
    [InlineData("", "Correct#1")]
    [InlineData("2023-00000", "anything")]   // account with no password set
    [InlineData("2020-00001", "Student#99")] // inactive
    public void Login_RejectsBadCredentials_WithOneGenericMessage(string who, string password)
    {
        var result = _auth.Login(who, password);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid ID or password.", result.Error);
    }

    [Fact]
    public void Login_ChecksTheRoleOnlyAfterThePassword_AndSaysNothingExtra()
    {
        var wrongRole = _auth.Login("2023-01045", "Student#99", ["finance"]);
        var rightRole = _auth.Login("2023-01045", "Student#99", ["student", "faculty"]);

        Assert.Equal("Invalid ID or password.", wrongRole.Error);
        Assert.True(rightRole.Succeeded);
    }

    [Fact]
    public void FiveWrongPasswords_LockTheAccount_EvenForTheRightPassword()
    {
        for (var i = 0; i < AuthStore.MaxFailures; i++) Assert.False(_auth.Login("FIN-1", "bad").Succeeded);

        var locked = _auth.Login("FIN-1", "Correct#1");

        Assert.False(locked.Succeeded);
        Assert.Contains("Too many failed attempts", locked.Error);
        Assert.NotNull(Row(1)["locked_until"]);
    }

    [Fact]
    public void ALockThatHasExpired_AllowsSignInAgain_AndResetsTheCounters()
    {
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().First(u => u["id"]!.GetValue<int>() == 1)["locked_until"] = DateTime.UtcNow.AddMinutes(-1).ToString("O"));
        _auth.Login("FIN-1", "bad");

        var result = _auth.Login("FIN-1", "Correct#1");

        Assert.True(result.Succeeded);
        Assert.Equal(0, Row(1)["failed_logins"]!.GetValue<int>());
        Assert.Null(Row(1)["locked_until"]);
    }

    [Fact]
    public void ASuccessfulLogin_ClearsEarlierFailures()
    {
        _auth.Login("FIN-1", "bad");
        _auth.Login("FIN-1", "bad");
        Assert.Equal(2, Row(1)["failed_logins"]!.GetValue<int>());

        _auth.Login("FIN-1", "Correct#1");

        Assert.Equal(0, Row(1)["failed_logins"]!.GetValue<int>());
    }

    [Fact]
    public void ChangePassword_NeedsTheCurrentOne_AndALongEnoughNewOne()
    {
        Assert.Equal("The current password is incorrect.", _auth.ChangePassword(1, "nope", "BrandNew#1"));
        Assert.Contains("at least 8", _auth.ChangePassword(1, "Correct#1", "short"));
        Assert.Null(_auth.ChangePassword(1, "Correct#1", "BrandNew#1"));

        Assert.False(_auth.Login("FIN-1", "Correct#1").Succeeded);
        Assert.True(_auth.Login("FIN-1", "BrandNew#1").Succeeded);
        Assert.Equal("Account not found.", _auth.ChangePassword(99, "x", "BrandNew#1"));
    }

    [Fact]
    public void SetPassword_ClearsALock_AndGivesAPasswordlessAccountOne()
    {
        for (var i = 0; i < AuthStore.MaxFailures; i++) _auth.Login("FIN-1", "bad");

        Assert.Null(_auth.SetPassword(1, "Reset#12345"));
        Assert.Null(_auth.SetPassword(6, "First#12345"));

        Assert.True(_auth.Login("FIN-1", "Reset#12345").Succeeded);
        Assert.True(_auth.Login("2023-00000", "First#12345").Succeeded);
        Assert.NotNull(_auth.SetPassword(6, "tiny"));
    }

    [Fact]
    public void Passwords_AreStoredAsBCryptHashes_NeverPlainText()
    {
        var hash = Row(1)["password_hash"]!.GetValue<string>();

        Assert.StartsWith("$2", hash);
        Assert.DoesNotContain("Correct#1", File.ReadAllText(_path));
    }

    [Fact]
    public void Find_ReturnsTheAccountOrNull()
    {
        Assert.Equal("finance", _auth.Find(1)!.Role);
        Assert.Null(_auth.Find(404));
    }
}
