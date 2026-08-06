using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace RLogistics.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddRLogisticsSecurity(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AuthenticationOptions>(config.GetSection(AuthenticationOptions.SectionName));
        var opts = config.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                   ?? new AuthenticationOptions();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPermissionService, PermissionService>();

        var mode = opts.ResolvedMode;
        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AuthSchemes.PolicyScheme;
            options.DefaultChallengeScheme = AuthSchemes.PolicyScheme;
        });

        authBuilder.AddPolicyScheme(AuthSchemes.PolicyScheme, "RLogistics selectable auth", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (mode is ApiAuthMode.ApiKey)
                    return AuthSchemes.ApiKey;
                if (mode is ApiAuthMode.Jwt)
                    return AuthSchemes.Jwt;

                if (context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName))
                    return AuthSchemes.ApiKey;
                return AuthSchemes.Jwt;
            };
        });

        authBuilder.AddJwtBearer(AuthSchemes.Jwt, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = opts.Jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = opts.Jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Jwt.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/json";
                    return ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide Bearer JWT or X-Api-Key (depending on Authentication:Mode)." });
                }
            };
        });

        authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            AuthSchemes.ApiKey, _ => { });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = null;
            foreach (var permission in RLogisticsPermissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser()
                        .RequireClaim(RLogisticsPermissions.ClaimType, permission));
            }

            options.AddPolicy("CoordinatorOrAdmin", p =>
                p.RequireAuthenticatedUser().RequireRole(nameof(Domain.UserRole.Coordinator), nameof(Domain.UserRole.Admin)));
            options.AddPolicy("AdminOnly", p =>
                p.RequireAuthenticatedUser().RequireRole(nameof(Domain.UserRole.Admin)));
        });

        return services;
    }
}

public interface IPermissionService
{
    bool Has(string permission);
    IReadOnlyCollection<string> CurrentPermissions();
}

public sealed class PermissionService(IHttpContextAccessor http) : IPermissionService
{
    public bool Has(string permission) =>
        http.HttpContext?.User?.HasClaim(RLogisticsPermissions.ClaimType, permission) == true;

    public IReadOnlyCollection<string> CurrentPermissions() =>
        http.HttpContext?.User?.FindAll(RLogisticsPermissions.ClaimType).Select(c => c.Value).ToArray()
        ?? Array.Empty<string>();
}
