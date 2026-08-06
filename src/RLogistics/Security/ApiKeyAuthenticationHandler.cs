using System.Security.Claims;
using System.Text.Encodings.Web;
using RLogistics.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RLogistics.Security;

/// <summary>Enterprise-style API key authentication (machines / partner systems).</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthenticationOptions> authOptions,
    RLogisticsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return AuthenticateResult.NoResult();
        }

        var key = raw.ToString().Trim();
        var entry = authOptions.Value.ApiKeys
            .FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.Ordinal));

        if (entry is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == entry.Email);

        if (user is null)
            return AuthenticateResult.Fail("API key maps to unknown user.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("mdt_user_id", user.Id.ToString()),
            new("auth_scheme", AuthSchemes.ApiKey)
        };
        foreach (var p in PermissionCatalog.ForRole(user.Role))
            claims.Add(new Claim(RLogisticsPermissions.ClaimType, p));

        var identity = new ClaimsIdentity(claims, AuthSchemes.ApiKey);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthSchemes.ApiKey);
        return AuthenticateResult.Success(ticket);
    }
}
