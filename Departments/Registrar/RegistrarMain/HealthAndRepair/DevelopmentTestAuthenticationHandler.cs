using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RegistrarMain.HealthAndRepair;

public sealed class DevelopmentTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var studentId = configuration["DevelopmentAuthentication:StudentId"];
        if (string.IsNullOrWhiteSpace(studentId))
            return Task.FromResult(AuthenticateResult.Fail("DevelopmentAuthentication:StudentId is not configured."));

        var role = Request.Headers.TryGetValue("X-Test-Role", out var requestedRole) && !string.IsNullOrWhiteSpace(requestedRole)
            ? requestedRole.ToString()
            : "Student";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, studentId),
            new Claim("StudentId", studentId),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
