using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RLogistics.Contracts;
using RLogistics.Tests.Infrastructure;

namespace RLogistics.Tests.Integration;

public class NotificationsApiTests : IClassFixture<RLogisticsWebApplicationFactory>
{
    private readonly RLogisticsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NotificationsApiTests(RLogisticsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Status_is_mock_by_default()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.AdminKey);
        var res = await client.GetAsync("/api/notifications/status");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("Mock");
        json.Should().Match(s => s.Contains("clientIdConfigured") && s.Contains("false"));
    }

    [Fact]
    public async Task Test_email_and_teams_write_outboxes()
    {
        var admin = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.AdminKey);

        var email = await admin.PostAsJsonAsync("/api/notifications/test-email", new { to = "user@demo.local" });
        email.StatusCode.Should().Be(HttpStatusCode.OK);

        var teams = await admin.PostAsync("/api/notifications/test-teams", null);
        teams.StatusCode.Should().Be(HttpStatusCode.OK);

        var outbox = await admin.GetAsync("/api/email-outbox");
        outbox.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await outbox.Content.ReadFromJsonAsync<List<EmailOutboxDto>>(JsonOpts);
        rows!.Should().Contain(r => r.Subject.Contains("Test") || r.ToAddress == "user@demo.local");

        var teamsOut = await admin.GetAsync("/api/notifications/teams-outbox");
        teamsOut.StatusCode.Should().Be(HttpStatusCode.OK);
        var trows = await teamsOut.Content.ReadAsStringAsync();
        trows.Should().Contain("RLogistics test");
    }

    [Fact]
    public async Task Graph_login_without_clientId_is_bad_request()
    {
        var admin = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.AdminKey);
        var res = await admin.PostAsync("/api/notifications/graph/login", null);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task User_cannot_send_test_email()
    {
        var user = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.UserKey);
        var res = await user.PostAsJsonAsync("/api/notifications/test-email", new { to = "a@b.com" });
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
