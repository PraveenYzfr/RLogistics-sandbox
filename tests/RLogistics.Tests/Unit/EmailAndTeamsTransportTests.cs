using FluentAssertions;
using RLogistics.Abstractions;
using RLogistics.Caching;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Integrations.Notifications;
using RLogistics.Patterns.Adapter;
using RLogistics.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace RLogistics.Tests.Unit;

public class EmailAndTeamsTransportTests : IAsyncLifetime
{
    private readonly RLogisticsDbContext _db;

    public EmailAndTeamsTransportTests()
    {
        var opts = new DbContextOptionsBuilder<RLogisticsDbContext>()
            .UseInMemoryDatabase("EmailTeams_" + Guid.NewGuid().ToString("N"))
            .Options;
        _db = new RLogisticsDbContext(opts);
        _db.EmailTemplates.Add(new EmailTemplate
        {
            Code = "StatusChanged",
            SubjectTemplate = "RLogistics {{RequestNumber}} status: {{StatusTo}}",
            BodyTemplate = "Hello {{ContactName}} — {{StatusFrom}} to {{StatusTo}} at {{Site}}",
            IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = 1, Email = "user@demo.local", DisplayName = "Alex", Role = UserRole.User
        });
        _db.SaveChanges();
    }

    public async Task InitializeAsync() => await Task.CompletedTask;
    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MockOutbox_writes_email_row()
    {
        var transport = new MockOutboxEmailTransport(_db);
        await transport.SendAsync(new EmailMessage(
            "a@b.com", "Subj", "Body", 99, "StatusChanged", "Created", "Assigned"));

        _db.EmailOutbox.Should().ContainSingle(e => e.ToAddress == "a@b.com" && e.Subject == "Subj");
    }

    [Fact]
    public async Task Composite_Mock_mode_only_outbox_no_graph_exception()
    {
        var mock = new MockOutboxEmailTransport(_db);
        var tokens = new PersonalGraphTokenStore(
            Options.Create(new NotificationOptions()),
            NullLogger<PersonalGraphTokenStore>.Instance);
        var graph = new GraphMailTransport(
            Options.Create(new NotificationOptions { Mode = "PersonalMicrosoft", Graph = new GraphOptions { ClientId = "" } }),
            NullLogger<GraphMailTransport>.Instance,
            tokens);
        var composite = new CompositeEmailTransport(
            mock,
            graph,
            Options.Create(new NotificationOptions { Mode = "Mock", AlwaysAuditToOutbox = true }),
            NullLogger<CompositeEmailTransport>.Instance);

        await composite.SendAsync(new EmailMessage("z@y.com", "T", "B", null, null, null, null));

        _db.EmailOutbox.Should().ContainSingle(e => e.ToAddress == "z@y.com");
    }

    [Fact]
    public async Task Teams_MockOutbox_writes_row()
    {
        var notifier = new CompositeTeamsNotifier(
            _db,
            new DummyHttpClientFactory(),
            Options.Create(new NotificationOptions
            {
                Teams = new TeamsOptions { Provider = "MockOutbox" }
            }),
            new PersonalGraphTokenStore(Options.Create(new NotificationOptions()), NullLogger<PersonalGraphTokenStore>.Instance),
            NullLogger<CompositeTeamsNotifier>.Instance);

        await notifier.NotifyAsync(new TeamsMessage("Title", "Body text", 7));

        _db.TeamsOutbox.Should().ContainSingle(t => t.Title == "Title" && t.ProviderResult == "mock-outbox");
    }

    [Fact]
    public async Task EmailNotificationService_status_change_writes_email_and_teams()
    {
        var user = await _db.Users.FirstAsync();
        var request = new DisposalRequest
        {
            RequestNumber = "RLogistics-T-1",
            Site = "Lab",
            ContactName = "Alex",
            ContactEmail = "user@demo.local",
            PickupAddressLine1 = "1 Main",
            PickupCity = "Charlotte",
            PickupCountry = "USA",
            DispositionType = DispositionType.Sanitize,
            RequestType = RequestType.UsSurplus,
            Status = RequestStatus.Created,
            RequestorUserId = user.Id,
            Requestor = user,
            Assets =
            [
                new AssetLine
                {
                    AssetType = "Laptop", Manufacturer = "Dell", Model = "1", Quantity = 1,
                    DeviceGuid = "guid-1"
                }
            ]
        };
        _db.Requests.Add(request);
        await _db.SaveChangesAsync();

        var mock = new MockOutboxEmailTransport(_db);
        var tokens = new PersonalGraphTokenStore(Options.Create(new NotificationOptions()), NullLogger<PersonalGraphTokenStore>.Instance);
        var graph = new GraphMailTransport(
            Options.Create(new NotificationOptions { Mode = "Mock" }),
            NullLogger<GraphMailTransport>.Instance, tokens);
        var emailTransport = new CompositeEmailTransport(
            mock, graph,
            Options.Create(new NotificationOptions { Mode = "Mock" }),
            NullLogger<CompositeEmailTransport>.Instance);
        var teams = new CompositeTeamsNotifier(
            _db, new DummyHttpClientFactory(),
            Options.Create(new NotificationOptions { Teams = new TeamsOptions { Provider = "MockOutbox" }, NotifyTeamsOnEmail = true }),
            tokens, NullLogger<CompositeTeamsNotifier>.Instance);

        var svc = new EmailNotificationService(
            _db, emailTransport, teams,
            Options.Create(new NotificationOptions { NotifyTeamsOnEmail = true }));

        await svc.SendStatusChangeAsync(request, RequestStatus.Created, RequestStatus.Assigned);

        _db.EmailOutbox.Should().Contain(e => e.Subject.Contains("RLogistics-T-1") || e.Body.Contains("Assigned"));
        _db.TeamsOutbox.Should().Contain(t => t.Title.Contains("RLogistics-T-1"));
    }

    [Fact]
    public async Task DistributedCache_roundtrip()
    {
        var mem = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new DistributedCacheService(
            mem,
            Options.Create(new RedisOptions { DefaultTtlSeconds = 30 }),
            NullLogger<DistributedCacheService>.Instance);

        await cache.SetAsync("k1", new { A = 1 });
        var got = await cache.GetAsync<Dictionary<string, int>>("k1");
        // System.Text.Json deserializes object as Dictionary with JsonElement values sometimes —
        // use a concrete type
        got.Should().NotBeNull();
        await cache.RemoveAsync("k1");
        (await cache.GetAsync<Dictionary<string, int>>("k1")).Should().BeNull();
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
