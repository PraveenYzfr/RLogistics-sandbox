using FluentValidation;
using RLogistics.Contracts;
using RLogistics.Data;
using RLogistics.Patterns.Strategy;
using RLogistics.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RLogistics.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    RLogisticsDbContext db,
    IJwtTokenService jwt,
    IOptions<AuthenticationOptions> authOptions,
    IAuthPresentationStrategyFactory authFactory,
    IValidator<LoginRequestDto> loginValidator) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponseDto>> Token([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        var validation = await loginValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var mode = authOptions.Value.ResolvedMode;
        if (mode is ApiAuthMode.ApiKey)
            return BadRequest(new { error = "Authentication:Mode is ApiKey — JWT login disabled. Use X-Api-Key." });

        if (!string.Equals(dto.Password, authOptions.Value.DemoPassword, StringComparison.Ordinal))
            return Unauthorized(new { error = "Invalid email or password." });

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim(), ct);
        if (user is null)
            return Unauthorized(new { error = "Invalid email or password." });

        var token = jwt.CreateToken(user);
        var perms = PermissionCatalog.ForRole(user.Role).ToArray();
        return Ok(new TokenResponseDto(
            token,
            "Bearer",
            authOptions.Value.Jwt.ExpiresMinutes,
            user.Email,
            user.Role.ToString(),
            perms,
            mode.ToString()));
    }

    [HttpGet("schemes")]
    [AllowAnonymous]
    public ActionResult<object> Schemes()
    {
        var strategies = authFactory.CreateAllEnabled();
        return Ok(new
        {
            mode = authOptions.Value.ResolvedMode.ToString(),
            schemes = strategies.Select(s => new { s.SchemeName, how = s.DescribeHowToAuthenticate() }),
            permissionsCatalog = RLogisticsPermissions.All
        });
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> Me()
    {
        var perms = User.FindAll(RLogisticsPermissions.ClaimType).Select(c => c.Value).ToArray();
        return Ok(new
        {
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            name = User.Identity?.Name,
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
            scheme = User.FindFirst("auth_scheme")?.Value,
            permissions = perms
        });
    }
}
