using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Tests.Infrastructure;

namespace RLogistics.Tests.Integration;

public class AuthApiTests : IClassFixture<RLogisticsWebApplicationFactory>
{
    private readonly RLogisticsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AuthApiTests(RLogisticsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Schemes_is_public()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/auth/schemes");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Match(s => s.Contains("JwtAndApiKey", StringComparison.OrdinalIgnoreCase)
            || s.Contains("jwt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Token_issues_jwt_for_demo_password()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/token", new
        {
            email = "coord1@demo.local",
            password = "Demo@RLogistics2026!"
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<TokenResponseDto>(JsonOpts);
        dto!.AccessToken.Should().NotBeNullOrWhiteSpace();
        dto.Role.Should().Be(nameof(UserRole.Coordinator));
        dto.Permissions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Token_rejects_bad_password()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/token", new
        {
            email = "coord1@demo.local",
            password = "wrong-password-xx"
        });
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_works_with_api_key()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.CoordKey);
        var res = await client.GetAsync("/api/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("coord1@demo.local");
    }

    [Fact]
    public async Task Me_works_with_bearer()
    {
        var client = _factory.CreateClient();
        var tokenRes = await client.PostAsJsonAsync("/api/auth/token", new
        {
            email = "admin@demo.local",
            password = "Demo@RLogistics2026!"
        });
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenDto = await tokenRes.Content.ReadFromJsonAsync<TokenResponseDto>(JsonOpts);
        tokenDto!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Fresh client so only Bearer is present (no accidental sticky headers)
        var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenDto.AccessToken);
        var res = await authed.GetAsync("/api/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("admin@demo.local");
    }

    [Fact]
    public async Task Users_list_anonymous_ok()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/users");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await res.Content.ReadFromJsonAsync<List<UserDto>>(JsonOpts);
        users!.Should().Contain(u => u.Email == "user@demo.local");
        users.Should().HaveCountGreaterThanOrEqualTo(4);
    }
}
