using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSystem.Data.Auth;

public sealed class CampusAuthOptions
{
    /// <summary>Shown on the sign-in page, e.g. "Finance Office".</summary>
    public required string AppName { get; init; }

    /// <summary>Cookie names must differ per app: browsers share cookies across ports on localhost.</summary>
    public required string CookieName { get; init; }

    /// <summary>The <c>users.role</c> values this app admits.</summary>
    public required string[] Roles { get; init; }

    /// <summary>Where a user lands after signing in with no return address. The default is the site root.</summary>
    public Func<string, string> HomeFor { get; init; } = _ => "/";
}

/// <summary>Requires a signed-in user whose role is one of the given roles, using the Campus cookie.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CampusRolesAttribute : AuthorizeAttribute
{
    public CampusRolesAttribute(params string[] roles)
    {
        AuthenticationSchemes = CampusAuth.Scheme;
        Roles = string.Join(",", roles);
    }
}

/// <summary>
/// Cookie sign-in for the department apps, backed by <see cref="AuthStore"/>. Call <see cref="AddCampusAuth"/> while
/// building services, then <see cref="MapCampusAuth"/> after <c>UseAuthentication</c>/<c>UseAuthorization</c>.
/// Every page that has no [Authorize] attribute of its own still requires a signed-in user of an admitted role.
/// </summary>
public static class CampusAuth
{
    public const string Scheme = "Campus";
    public const string ServiceKeyHeader = "X-Campus-Service-Key";

    public static IServiceCollection AddCampusAuth(this IServiceCollection services, CampusAuthOptions options)
    {
        services.AddSingleton<AuthStore>();
        services.AddSingleton(options);
        services.AddAntiforgery(o => o.Cookie.Name = options.CookieName + ".af");
        services.AddAuthentication().AddCookie(Scheme, cookie =>
        {
            cookie.Cookie.Name = options.CookieName;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
            cookie.LoginPath = "/Login";
            cookie.AccessDeniedPath = "/Login";
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.Redirect("/Login?denied=1");
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().RequireRole(options.Roles).Build());
        return services;
    }

    public static ClaimsPrincipal ToPrincipal(AuthUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role),
            new("IdNumber", user.IdNumber),
        };
        if (user.Role == "student") claims.Add(new Claim("StudentId", user.Id.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    public static int? GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string GetRole(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Role) ?? "";

    /// <summary>True when the request carries the configured <c>CampusSystem:ServiceKey</c>, for calls between apps.</summary>
    public static bool HasServiceKey(HttpContext context)
    {
        var expected = context.RequestServices.GetRequiredService<IConfiguration>()["CampusSystem:ServiceKey"];
        var given = context.Request.Headers[ServiceKeyHeader].ToString();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
    }

    /// <summary>
    /// For JSON endpoints: lets the request through with the service key, or with a signed-in user in one of the roles.
    /// Anything else gets 401 (not signed in) or 403 (wrong role), never a redirect to the sign-in page.
    /// </summary>
    public static RouteHandlerBuilder RequireRolesOrServiceKey(this RouteHandlerBuilder builder, params string[] roles) =>
        builder.AllowAnonymous().AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (HasServiceKey(context)) return await next(invocation);

            var result = await context.AuthenticateAsync(Scheme);
            if (!result.Succeeded || result.Principal is null) return Results.Json(new { error = "Sign in or supply a service key." }, statusCode: 401);
            return roles.Contains(result.Principal.GetRole(), StringComparer.OrdinalIgnoreCase)
                ? await next(invocation)
                : Results.Json(new { error = "Your role cannot use this endpoint." }, statusCode: 403);
        });

    public static IEndpointRouteBuilder MapCampusAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/Login", async (HttpContext context, CampusAuthOptions options) =>
            Results.Content(await RenderLogin(context, options, null, context.Request.Query["returnUrl"]), "text/html; charset=utf-8")).AllowAnonymous();

        app.MapPost("/Login", async (HttpContext context, CampusAuthOptions options, AuthStore auth, IAntiforgery antiforgery) =>
        {
            var form = await context.Request.ReadFormAsync();
            var returnUrl = form["returnUrl"].ToString();
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                return Results.Content(await RenderLogin(context, options, "The form expired. Please try again.", returnUrl), "text/html; charset=utf-8");
            }

            var result = auth.Login(form["identifier"].ToString(), form["password"].ToString(), options.Roles);
            if (!result.Succeeded)
                return Results.Content(await RenderLogin(context, options, result.Error, returnUrl), "text/html; charset=utf-8");

            await context.SignInAsync(Scheme, ToPrincipal(result.User!));
            var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
                ? returnUrl
                : options.HomeFor(result.User!.Role);
            return Results.Redirect(target);
        }).AllowAnonymous();

        app.MapMethods("/Logout", ["GET", "POST"], async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Redirect("/Login");
        }).AllowAnonymous();

        app.MapGet("/ChangePassword", async (HttpContext context, CampusAuthOptions options) =>
            Results.Content(await RenderChangePassword(context, options, null, false), "text/html; charset=utf-8"))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = Scheme });

        app.MapPost("/ChangePassword", async (HttpContext context, CampusAuthOptions options, AuthStore auth, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                return Results.Content(await RenderChangePassword(context, options, "The form expired. Please try again.", false), "text/html; charset=utf-8");
            }

            var form = await context.Request.ReadFormAsync();
            var userId = context.User.GetUserId();
            string? error = userId is null ? "Sign in again." : form["newPassword"].ToString() != form["confirmPassword"].ToString()
                ? "The new passwords do not match."
                : auth.ChangePassword(userId.Value, form["currentPassword"].ToString(), form["newPassword"].ToString());
            return Results.Content(await RenderChangePassword(context, options, error, error is null), "text/html; charset=utf-8");
        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = Scheme });

        return app;
    }

    private static async Task<string> RenderLogin(HttpContext context, CampusAuthOptions options, string? error, string? returnUrl)
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        var denied = context.Request.Query.ContainsKey("denied") && error is null
            ? "That account is not allowed to open this page. Sign in with a different account." : null;
        var message = error ?? denied;
        var banner = message is null ? "" : $"<p class=\"error\">{Enc(message)}</p>";
        await Task.CompletedTask;
        return Page($"Sign in · {options.AppName}", $@"
<h1>{Enc(options.AppName)}</h1>
<p class=""sub"">Sign in with your ID number or email.</p>
{banner}
<form method=""post"" action=""/Login"">
  <input type=""hidden"" name=""{Enc(tokens.FormFieldName)}"" value=""{Enc(tokens.RequestToken ?? "")}"" />
  <input type=""hidden"" name=""returnUrl"" value=""{Enc(returnUrl ?? "")}"" />
  <label>ID number or email<input name=""identifier"" autocomplete=""username"" required autofocus /></label>
  <label>Password<input name=""password"" type=""password"" autocomplete=""current-password"" required /></label>
  <button type=""submit"">Sign in</button>
</form>");
    }

    private static async Task<string> RenderChangePassword(HttpContext context, CampusAuthOptions options, string? error, bool done)
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        var banner = done ? "<p class=\"ok\">Password changed.</p>" : error is null ? "" : $"<p class=\"error\">{Enc(error)}</p>";
        await Task.CompletedTask;
        return Page($"Change password · {options.AppName}", $@"
<h1>Change password</h1>
<p class=""sub"">Signed in as {Enc(context.User.Identity?.Name ?? "")}. At least {AuthStore.MinPasswordLength} characters.</p>
{banner}
<form method=""post"" action=""/ChangePassword"">
  <input type=""hidden"" name=""{Enc(tokens.FormFieldName)}"" value=""{Enc(tokens.RequestToken ?? "")}"" />
  <label>Current password<input name=""currentPassword"" type=""password"" autocomplete=""current-password"" required /></label>
  <label>New password<input name=""newPassword"" type=""password"" autocomplete=""new-password"" required /></label>
  <label>Repeat new password<input name=""confirmPassword"" type=""password"" autocomplete=""new-password"" required /></label>
  <button type=""submit"">Change password</button>
  <a href=""/"" class=""link"">Back</a>
</form>");
    }

    private static string Page(string title, string body) => $@"<!DOCTYPE html>
<html lang=""en""><head><meta charset=""utf-8"" /><meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>{Enc(title)}</title>
<style>
 body{{font-family:Segoe UI,system-ui,sans-serif;background:#eef2f9;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center}}
 .card{{background:#fff;border-radius:14px;box-shadow:0 8px 30px rgba(15,35,90,.12);padding:32px;width:min(380px,92vw)}}
 h1{{margin:0 0 4px;font-size:22px;color:#12307a}} .sub{{margin:0 0 18px;color:#64748b;font-size:13.5px}}
 label{{display:block;font-size:12.5px;font-weight:600;color:#334155;margin-bottom:12px}}
 input{{display:block;width:100%;box-sizing:border-box;margin-top:5px;padding:10px 12px;border:1.5px solid #cbd5e1;border-radius:8px;font-size:14px}}
 button{{width:100%;padding:11px;border:0;border-radius:8px;background:#12307a;color:#fff;font-weight:700;font-size:14px;cursor:pointer}}
 .error{{background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;border-radius:8px;padding:9px 12px;font-size:13px}}
 .ok{{background:#f0fdf4;color:#15803d;border:1px solid #bbf7d0;border-radius:8px;padding:9px 12px;font-size:13px}}
 .link{{display:block;text-align:center;margin-top:12px;font-size:13px;color:#12307a}}
</style></head><body><div class=""card"">{body}</div></body></html>";

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");
}
