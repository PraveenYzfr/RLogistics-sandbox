using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Tests.Infrastructure;

namespace RLogistics.Tests.Integration;

public class RequestsApiTests : IClassFixture<RLogisticsWebApplicationFactory>
{
    private readonly RLogisticsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public RequestsApiTests(RLogisticsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task List_requires_auth()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/requests");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_as_coordinator_returns_seeded()
    {
        var client = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.CoordKey);
        var res = await client.GetAsync("/api/requests");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await res.Content.ReadFromJsonAsync<List<RequestSummaryDto>>(JsonOpts);
        list!.Should().NotBeEmpty();
        list.Should().Contain(r => r.RequestNumber.StartsWith("RLogistics-"));
    }

    [Fact]
    public async Task Create_get_assign_status_plan_quotes_flow()
    {
        var userClient = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.UserKey);
        var coordClient = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.CoordKey);

        var createRes = await userClient.PostAsJsonAsync("/api/requests", TestData.ValidCreate(site: "Integration Lab"));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts);
        created!.Id.Should().BeGreaterThan(0);
        created.Status.Should().Be(RequestStatus.Created);
        created.Assets.Should().NotBeEmpty();
        created.Assets[0].Manufacturer.Should().Be("Dell");

        var get = await coordClient.GetAsync($"/api/requests/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        // Find coord user id
        var users = await (await coordClient.GetAsync("/api/users")).Content.ReadFromJsonAsync<List<UserDto>>(JsonOpts);
        var coordId = users!.First(u => u.Email == "coord1@demo.local").Id;

        var assign = await coordClient.PostAsJsonAsync($"/api/requests/{created.Id}/assign",
            new AssignRequestDto(coordId));
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await assign.Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts);
        assigned!.Status.Should().Be(RequestStatus.Assigned);

        var vendors = await (await coordClient.GetAsync("/api/vendors")).Content.ReadFromJsonAsync<List<VendorDto>>(JsonOpts);
        var transport = vendors!.First(v => v.Type == VendorType.Transport);
        var processing = vendors.First(v => v.Type == VendorType.Processing);

        var plan = await coordClient.PostAsJsonAsync($"/api/requests/{created.Id}/plan", new PlanRequestDto(
            TransportVendorId: transport.Id,
            ProcessingVendorId: processing.Id,
            ScheduledPickupDate: DateTime.UtcNow.Date.AddDays(3),
            ScheduledPickupSlot: PickupSlots.All[0],
            MarkPickupScheduled: true,
            ExpectedDeviceReturnDate: DateTime.UtcNow.Date.AddDays(1)));
        plan.StatusCode.Should().Be(HttpStatusCode.OK);

        var quotes = await coordClient.PostAsync($"/api/requests/{created.Id}/vendor-quotes", null);
        quotes.StatusCode.Should().Be(HttpStatusCode.OK);
        var quoteBody = await quotes.Content.ReadAsStringAsync();
        quoteBody.Should().Contain("sent");

        var status = await coordClient.PatchAsJsonAsync($"/api/requests/{created.Id}/status",
            new UpdateStatusDto(RequestStatus.PickedUp, "Out for haul"));
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        (await status.Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts))!.Status
            .Should().Be(RequestStatus.PickedUp);
    }

    [Fact]
    public async Task Clarification_and_reply()
    {
        var userClient = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.UserKey);
        var coordClient = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.CoordKey);

        var created = await (await userClient.PostAsJsonAsync("/api/requests",
            TestData.ValidCreate(site: "Clarify Site"))).Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts);

        var users = await (await coordClient.GetAsync("/api/users")).Content.ReadFromJsonAsync<List<UserDto>>(JsonOpts);
        var coordId = users!.First(u => u.Email == "coord1@demo.local").Id;
        await coordClient.PostAsJsonAsync($"/api/requests/{created!.Id}/assign", new AssignRequestDto(coordId));

        var clarify = await coordClient.PostAsJsonAsync($"/api/requests/{created.Id}/clarifications",
            new ClarificationDto("What is the dock access code?"));
        clarify.StatusCode.Should().Be(HttpStatusCode.OK);
        var held = await clarify.Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts);
        held!.Status.Should().Be(RequestStatus.OnHold);
        held.Clarifications.Should().NotBeEmpty();
        var cid = held.Clarifications.First().Id;

        var reply = await userClient.PostAsJsonAsync(
            $"/api/requests/{created.Id}/clarifications/{cid}/reply",
            new ClarificationReplyDto("Dock code is 1234"));
        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reply.Content.ReadFromJsonAsync<RequestDetailDto>(JsonOpts))!.Status
            .Should().Be(RequestStatus.Assigned);
    }

    [Fact]
    public async Task User_cannot_list_others_via_coord_only_ops()
    {
        var userClient = _factory.CreateClientAsApiKey(RLogisticsWebApplicationFactory.UserKey);
        var res = await userClient.PostAsJsonAsync("/api/requests/1/assign", new AssignRequestDto(2));
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
