using FluentAssertions;
using RLogistics.Domain;
using RLogistics.Patterns.Strategy;
using RLogistics.Security;
using Microsoft.Extensions.Options;

namespace RLogistics.Tests.Unit;

public class StrategyTests
{
    [Fact]
    public void Disposition_resolver_sanitize_and_destroy()
    {
        var resolver = new DispositionMessageResolver(
        [
            new SanitizeMessageStrategy(),
            new DestroyMessageStrategy()
        ]);

        resolver.ResolveHeadline(DispositionType.Sanitize, "RLogistics-1")
            .Should().Contain("Sanitize").And.Contain("RLogistics-1");
        resolver.ResolveHeadline(DispositionType.Destroy, "RLogistics-2")
            .Should().Contain("Destroy").And.Contain("RLogistics-2");
    }

    [Theory]
    [InlineData("Jwt", "JWT Bearer")]
    [InlineData("ApiKey", "API Key")]
    [InlineData("JwtAndApiKey", "JWT Bearer")]
    public void Auth_presentation_primary(string mode, string scheme)
    {
        var factory = new AuthPresentationStrategyFactory(
            Options.Create(new AuthenticationOptions { Mode = mode }));
        factory.CreatePrimary().SchemeName.Should().Be(scheme);
    }

    [Fact]
    public void Auth_presentation_JwtAndApiKey_lists_both()
    {
        var factory = new AuthPresentationStrategyFactory(
            Options.Create(new AuthenticationOptions { Mode = "JwtAndApiKey" }));
        factory.CreateAllEnabled().Should().HaveCount(2);
    }
}
