using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using RLogistics.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RLogistics.Security;

public interface IJwtTokenService
{
    string CreateToken(AppUser user);
    ClaimsPrincipal? ValidateToken(string token);
}

public sealed class JwtTokenService(IOptions<AuthenticationOptions> options) : IJwtTokenService
{
    private readonly AuthenticationOptions _opts = options.Value;

    public string CreateToken(AppUser user)
    {
        var jwt = _opts.Jwt;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var permissions = PermissionCatalog.ForRole(user.Role);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("mdt_user_id", user.Id.ToString()),
            new("auth_scheme", AuthSchemes.Jwt)
        };
        claims.AddRange(permissions.Select(p => new Claim(RLogisticsPermissions.ClaimType, p)));

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(jwt.ExpiresMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var jwt = _opts.Jwt;
        var handler = new JwtSecurityTokenHandler();
        try
        {
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
        }
        catch
        {
            return null;
        }
    }
}

public static class AuthSchemes
{
    public const string Jwt = "Bearer";
    public const string ApiKey = "ApiKey";
    public const string PolicyScheme = "MdtAuth";
}
