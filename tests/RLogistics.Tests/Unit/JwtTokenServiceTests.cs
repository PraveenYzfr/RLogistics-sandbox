using FluentAssertions;
using RLogistics.Domain;
using RLogistics.Security;
using Microsoft.Extensions.Options;

namespace RLogistics.Tests.Unit;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateSut() =>
        new(Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Issuer = "test-iss",
                Audience = "test-aud",
                SigningKey = "RLogistics-TEST-SIGNING-KEY-32chars-min!!",
                ExpiresMinutes = 60
            }
        }));

    [Fact]
    public void Create_and_validate_roundtrip()
    {
        var sut = CreateSut();
        var user = new AppUser
        {
            Id = 42,
            Email = "coord1@demo.local",
            DisplayName = "Casey",
            Role = UserRole.Coordinator
        };

        var token = sut.CreateToken(user);
        token.Should().NotBeNullOrWhiteSpace();

        var principal = sut.ValidateToken(token);
        principal.Should().NotBeNull();
        principal!.FindFirst("mdt_user_id")!.Value.Should().Be("42");
        principal.IsInRole(nameof(UserRole.Coordinator)).Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_tampered_token()
    {
        var sut = CreateSut();
        var token = sut.CreateToken(new AppUser
        {
            Id = 1, Email = "a@b.com", DisplayName = "A", Role = UserRole.User
        });
        var bad = token[..^4] + "XXXX";
        sut.ValidateToken(bad).Should().BeNull();
    }
}
