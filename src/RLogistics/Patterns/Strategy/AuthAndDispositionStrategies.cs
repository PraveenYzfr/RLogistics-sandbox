using RLogistics.Domain;

namespace RLogistics.Patterns.Strategy;

/// <summary>
/// Strategy — pluggable temperature-free authorization display / access resolution styles
/// for different client types (JWT vs API key consumers).
/// </summary>
public interface IAuthPresentationStrategy
{
    string SchemeName { get; }
    string DescribeHowToAuthenticate();
}

public sealed class JwtAuthPresentationStrategy : IAuthPresentationStrategy
{
    public string SchemeName => "JWT Bearer";
    public string DescribeHowToAuthenticate() =>
        "POST /api/auth/token with email+password, then Authorization: Bearer {token}";
}

public sealed class ApiKeyAuthPresentationStrategy : IAuthPresentationStrategy
{
    public string SchemeName => "API Key";
    public string DescribeHowToAuthenticate() =>
        "Send header X-Api-Key with a key from Authentication:ApiKeys config";
}

/// <summary>
/// Factory Method — select auth presentation strategy from configured mode.
/// </summary>
public interface IAuthPresentationStrategyFactory
{
    IAuthPresentationStrategy CreatePrimary();
    IReadOnlyList<IAuthPresentationStrategy> CreateAllEnabled();
}

public sealed class AuthPresentationStrategyFactory(Microsoft.Extensions.Options.IOptions<Security.AuthenticationOptions> options)
    : IAuthPresentationStrategyFactory
{
    public IAuthPresentationStrategy CreatePrimary()
    {
        return options.Value.ResolvedMode switch
        {
            Security.ApiAuthMode.ApiKey => new ApiKeyAuthPresentationStrategy(),
            _ => new JwtAuthPresentationStrategy()
        };
    }

    public IReadOnlyList<IAuthPresentationStrategy> CreateAllEnabled()
    {
        return options.Value.ResolvedMode switch
        {
            Security.ApiAuthMode.Jwt => [new JwtAuthPresentationStrategy()],
            Security.ApiAuthMode.ApiKey => [new ApiKeyAuthPresentationStrategy()],
            _ => [new JwtAuthPresentationStrategy(), new ApiKeyAuthPresentationStrategy()]
        };
    }
}

/// <summary>
/// Strategy for disposition-specific messaging (sanitize vs destroy) used by notifications.
/// </summary>
public interface IDispositionMessageStrategy
{
    bool CanHandle(DispositionType type);
    string Headline(string requestNumber);
}

public sealed class SanitizeMessageStrategy : IDispositionMessageStrategy
{
    public bool CanHandle(DispositionType type) => type == DispositionType.Sanitize;
    public string Headline(string requestNumber) => $"Sanitize workflow active for {requestNumber}";
}

public sealed class DestroyMessageStrategy : IDispositionMessageStrategy
{
    public bool CanHandle(DispositionType type) => type == DispositionType.Destroy;
    public string Headline(string requestNumber) => $"Destroy/chain-of-custody workflow for {requestNumber}";
}

public sealed class DispositionMessageResolver(IEnumerable<IDispositionMessageStrategy> strategies)
{
    public string ResolveHeadline(DispositionType type, string requestNumber)
    {
        var s = strategies.FirstOrDefault(x => x.CanHandle(type));
        return s?.Headline(requestNumber) ?? $"Processing {requestNumber}";
    }
}
