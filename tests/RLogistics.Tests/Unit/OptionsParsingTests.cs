using FluentAssertions;
using RLogistics.Domain;
using RLogistics.Integrations.Notifications;
using RLogistics.Security;

namespace RLogistics.Tests.Unit;

public class OptionsParsingTests
{
    [Theory]
    [InlineData("Mock", NotificationChannelMode.Mock)]
    [InlineData("PersonalMicrosoft", NotificationChannelMode.PersonalMicrosoft)]
    [InlineData("personalmicrosoft", NotificationChannelMode.PersonalMicrosoft)]
    [InlineData("EnterpriseGraph", NotificationChannelMode.EnterpriseGraph)]
    [InlineData("garbage", NotificationChannelMode.Mock)]
    public void NotificationMode_parses(string mode, NotificationChannelMode expected)
    {
        new NotificationOptions { Mode = mode }.ResolvedMode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Jwt", ApiAuthMode.Jwt)]
    [InlineData("ApiKey", ApiAuthMode.ApiKey)]
    [InlineData("JwtAndApiKey", ApiAuthMode.JwtAndApiKey)]
    [InlineData("unknown", ApiAuthMode.JwtAndApiKey)]
    public void AuthMode_parses(string mode, ApiAuthMode expected)
    {
        new AuthenticationOptions { Mode = mode }.ResolvedMode.Should().Be(expected);
    }

    [Theory]
    [InlineData(RequestType.UsSurplus, "US Surplus")]
    [InlineData(RequestType.PointToPoint, "Point to Point")]
    [InlineData(RequestType.International, "International")]
    [InlineData(RequestType.RequestABox, "Request a Box")]
    public void RequestTypeLabels_display(RequestType type, string label)
    {
        RequestTypeLabels.Display(type).Should().Be(label);
    }
}
