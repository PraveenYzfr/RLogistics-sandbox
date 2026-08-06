using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RLogistics.Contracts;
using RLogistics.Tests.Infrastructure;

namespace RLogistics.Tests.Integration;

public class AdminAndVendorsApiTests : IClassFixture<RLogisticsWebApplicationFactory>
{
    private readonly RLogisticsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AdminAndVendorsApiTests(RLogisticsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Vendors_list_for_coordinator()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.CoordKey);
        var res = await client.GetAsync("/api/vendors");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var vendors = await res.Content.ReadFromJsonAsync<List<VendorDto>>(JsonOpts);
        vendors!.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Admin_email_templates_list()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.AdminKey);
        var res = await client.GetAsync("/api/admin/email-templates");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("StatusChanged");
    }

    [Fact]
    public async Task Correlation_header_echoed()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        req.Headers.Add("X-Correlation-Id", "test-corr-123");
        var res = await client.SendAsync(req);
        res.Headers.Should().Contain(h => h.Key == "X-Correlation-Id");
        res.Headers.GetValues("X-Correlation-Id").Should().Contain("test-corr-123");
    }

    [Fact]
    public async Task Security_headers_present_on_api()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.AdminKey);
        var res = await client.GetAsync("/api/notifications/status");
        res.Headers.Should().Contain(h => h.Key == "X-Content-Type-Options");
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task Home_page_returns_html()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("RLogistics");
    }
}
