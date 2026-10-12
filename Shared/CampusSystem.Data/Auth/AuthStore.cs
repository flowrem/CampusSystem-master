using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace CampusSystem.Data.Auth;

public sealed record AuthUser(int Id, string Name, string Role, string IdNumber, string Email);

public sealed record LoginResult(AuthUser? User, string? Error)
{
    public bool Succeeded => User is not null;
}

/// <summary>
/// Sign-in against the shared <c>users</c> collection. Passwords are BCrypt hashes in <c>password_hash</c>.
/// Five wrong passwords lock the account for fifteen minutes (<c>failed_logins</c>, <c>locked_until</c>).
/// Every department app uses this, so one account works in any app its role is allowed into.
/// </summary>
public sealed class AuthStore(CanonicalJsonStore store)
{
    public const int MaxFailures = 5;
    public const int MinPasswordLength = 8;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private const int WorkFactor = 10;

    private const string Invalid = "Invalid ID or password.";

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>
    /// Signs in by student/staff id number or email. <paramref name="allowedRoles"/> limits which accounts may enter this
    /// app; a valid password for a role that is not allowed gets the same message as a wrong password.
    /// </summary>
    public LoginResult Login(string identifier, string password, IReadOnlyCollection<string>? allowedRoles = null)
    {
        identifier = (identifier ?? "").Trim();
        if (identifier.Length == 0 || string.IsNullOrEmpty(password)) return new LoginResult(null, Invalid);

        LoginResult result = new(null, Invalid);
        store.UpdateRoot(root =>
        {
            var user = Users(root).FirstOrDefault(u => Same(Str(u, "student_id_number"), identifier) || Same(Str(u, "email"), identifier));
            if (user is null) return;

            var now = DateTimeOffset.UtcNow;
            if (DateTimeOffset.TryParse(Str(user, "locked_until"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until) && until > now)
            {
                result = new LoginResult(null, $"Too many failed attempts. Try again in {Math.Ceiling((until - now).TotalMinutes)} minute(s).");
                return;
            }

            var hash = Str(user, "password_hash");
            var passwordOk = hash.Length > 0 && SafeVerify(password, hash);
            if (!passwordOk)
            {
                var failures = Int(user, "failed_logins") + 1;
                user["failed_logins"] = failures;
                if (failures >= MaxFailures)
                {
                    user["locked_until"] = now.Add(LockDuration).ToString("O", CultureInfo.InvariantCulture);
                    user["failed_logins"] = 0;
                }
                return;
            }

            // The password is right. Say nothing more specific than "invalid" if this app does not admit the role.
            var role = Str(user, "role");
            if (!Same(Str(user, "status"), "active")) return;
            if (allowedRoles is not null && !allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase)) return;

            user["failed_logins"] = 0;
            user["locked_until"] = null;
            user["last_login"] = now.ToString("O", CultureInfo.InvariantCulture);
            result = new LoginResult(new AuthUser(Int(user, "id"), Str(user, "name"), role.ToLowerInvariant(), Str(user, "student_id_number"), Str(user, "email")), null);
        });
        return result;
    }

    /// <summary>Changes a password after checking the current one. Returns an error message, or null on success.</summary>
    public string? ChangePassword(int userId, string currentPassword, string newPassword)
    {
        if (newPassword is null || newPassword.Length < MinPasswordLength) return $"The new password must be at least {MinPasswordLength} characters.";

        string? error = "Account not found.";
        store.UpdateRoot(root =>
        {
            var user = Users(root).FirstOrDefault(u => Int(u, "id") == userId);
            if (user is null) return;
            var hash = Str(user, "password_hash");
            if (hash.Length == 0 || !SafeVerify(currentPassword ?? "", hash)) { error = "The current password is incorrect."; return; }

            user["password_hash"] = Hash(newPassword);
            user["password_changed_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            error = null;
        });
        return error;
    }

    /// <summary>Sets a password without the current one, for an administrator or a new account. Also clears any lockout.</summary>
    public string? SetPassword(int userId, string newPassword)
    {
        if (newPassword is null || newPassword.Length < MinPasswordLength) return $"The password must be at least {MinPasswordLength} characters.";

        string? error = "Account not found.";
        store.UpdateRoot(root =>
        {
            var user = Users(root).FirstOrDefault(u => Int(u, "id") == userId);
            if (user is null) return;
            user["password_hash"] = Hash(newPassword);
            user["failed_logins"] = 0;
            user["locked_until"] = null;
            error = null;
        });
        return error;
    }

    public AuthUser? Find(int userId)
    {
        var user = Users(store.ReadRoot()).FirstOrDefault(u => Int(u, "id") == userId);
        return user is null ? null : new AuthUser(userId, Str(user, "name"), Str(user, "role").ToLowerInvariant(), Str(user, "student_id_number"), Str(user, "email"));
    }

    private static bool SafeVerify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    private static IEnumerable<JsonObject> Users(JsonObject root) =>
        CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>();

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int Int(JsonObject o, string key) =>
        o[key] is not JsonValue v ? 0 : v.TryGetValue<int>(out var i) ? i : v.TryGetValue<long>(out var l) ? (int)l : 0;

    private static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
