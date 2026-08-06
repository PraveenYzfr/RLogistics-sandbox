namespace RLogistics.Security;

/// <summary>
/// Switchable API security modes.
/// Jwt — Bearer tokens only
/// ApiKey — X-Api-Key only
/// JwtAndApiKey — accept either (recommended lab mode)
/// </summary>
public enum ApiAuthMode
{
    Jwt = 0,
    ApiKey = 1,
    JwtAndApiKey = 2
}

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Jwt | ApiKey | JwtAndApiKey</summary>
    public string Mode { get; set; } = "JwtAndApiKey";

    public JwtOptions Jwt { get; set; } = new();
    public List<ApiKeyEntry> ApiKeys { get; set; } = [];
    /// <summary>Shared demo password for all seeded users (lab only).</summary>
    public string DemoPassword { get; set; } = "Demo@RLogistics2026!";

    public ApiAuthMode ResolvedMode =>
        Enum.TryParse<ApiAuthMode>(Mode, ignoreCase: true, out var m) ? m : ApiAuthMode.JwtAndApiKey;
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "rlogistics";
    public string Audience { get; set; } = "rlogistics-api";
    public string SigningKey { get; set; } = "RLogistics-DEV-SIGNING-KEY-CHANGE-IN-PROD-32chars!!";
    public int ExpiresMinutes { get; set; } = 120;
}

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
