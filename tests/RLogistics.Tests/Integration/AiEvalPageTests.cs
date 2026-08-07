using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RLogistics.Tests.Infrastructure;

namespace RLogistics.Tests.Integration;

public class AiEvalPageTests : IClassFixture<RLogisticsWebApplicationFactory>
{
    private readonly RLogisticsWebApplicationFactory _factory;

    public AiEvalPageTests(RLogisticsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AiEval_page_reachable_as_admin_persona_cookie_optional()
    {
        // Page redirects to Persona without cookie — still must not 500.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/Admin/AiEval");
        resp.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.Redirect,
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.Found,
            System.Net.HttpStatusCode.SeeOther);
    }
}
