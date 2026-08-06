using System.Security.Claims;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Security;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Services;

/// <summary>Scoped ambient user (UI cookie, JWT, or API key → hydrated principal).</summary>
public class PersonaContext
{
    public AppUser? Current { get; private set; }

    public void Set(AppUser user) => Current = user;

    public AppUser Require() =>
        Current ?? throw new UnauthorizedAccessException(
            "Not authenticated. Use Bearer JWT, X-Api-Key, or select a UI persona.");
}

/// <summary>
/// Hydrates PersonaContext from ClaimsPrincipal (JWT/API key) or UI cookie.
/// Keeps Razor Pages working while APIs use enterprise auth.
/// </summary>
public class PersonaMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-RLogistics-User-Id";
    public const string CookieName = "mdt_persona";

    public async Task InvokeAsync(HttpContext http, RLogisticsDbContext db, PersonaContext persona)
    {
        // Prefer authenticated principal (JWT / API key)
        if (http.User.Identity?.IsAuthenticated == true)
        {
            var idClaim = http.User.FindFirst("mdt_user_id")?.Value
                          ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var uid))
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
                if (user is not null)
                {
                    persona.Set(user);
                    await next(http);
                    return;
                }
            }
        }

        // UI persona cookie / optional header (dev & Razor)
        int? userId = null;
        if (http.Request.Headers.TryGetValue(HeaderName, out var header) &&
            int.TryParse(header.FirstOrDefault(), out var fromHeader))
        {
            userId = fromHeader;
        }
        else if (http.Request.Cookies.TryGetValue(CookieName, out var cookie) &&
                 int.TryParse(cookie, out var fromCookie))
        {
            userId = fromCookie;
        }

        if (userId is int id)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is not null)
            {
                persona.Set(user);
                // Also set a principal for [Authorize] on mixed pipelines when cookie is used
                if (http.User.Identity?.IsAuthenticated != true)
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new(ClaimTypes.Email, user.Email),
                        new(ClaimTypes.Name, user.DisplayName),
                        new(ClaimTypes.Role, user.Role.ToString()),
                        new("mdt_user_id", user.Id.ToString()),
                        new("auth_scheme", "CookiePersona")
                    };
                    foreach (var p in PermissionCatalog.ForRole(user.Role))
                        claims.Add(new Claim(RLogisticsPermissions.ClaimType, p));
                    http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "CookiePersona"));
                }
            }
        }

        await next(http);
    }
}
