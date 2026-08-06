using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RLogistics.Tests.Infrastructure;

/// <summary>
/// Boots full RLogistics with EF InMemory, Redis off, Mock notifications.
/// Unique DB name per factory instance for isolation.
/// </summary>
public sealed class RLogisticsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "RLogisticsTests_" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting is applied early enough for WebApplication.CreateBuilder DI.
        builder.UseSetting("Testing:InMemoryDatabaseName", _dbName);
        builder.UseSetting("ConnectionStrings:RLogistics", "");
        builder.UseSetting("Redis:Enabled", "false");
        builder.UseSetting("Genie:Enabled", "false");
        builder.UseSetting("Genie:BaseUrl", "http://127.0.0.1:9");
        builder.UseSetting("Notifications:Mode", "Mock");
        builder.UseSetting("Notifications:AlwaysAuditToOutbox", "true");
        builder.UseSetting("Notifications:NotifyTeamsOnEmail", "true");
        builder.UseSetting("Notifications:Graph:ClientId", "");
        builder.UseSetting("Notifications:Teams:Provider", "MockOutbox");
        builder.UseSetting("Authentication:Mode", "JwtAndApiKey");
        builder.UseSetting("Authentication:DemoPassword", "Demo@RLogistics2026!");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Testing:InMemoryDatabaseName"] = _dbName,
                ["ConnectionStrings:RLogistics"] = "",
                ["Redis:Enabled"] = "false",
                ["Genie:Enabled"] = "false",
                ["Genie:BaseUrl"] = "http://127.0.0.1:9",
                ["Notifications:Mode"] = "Mock",
                ["Notifications:AlwaysAuditToOutbox"] = "true",
                ["Notifications:NotifyTeamsOnEmail"] = "true",
                ["Notifications:Graph:ClientId"] = "",
                ["Notifications:Teams:Provider"] = "MockOutbox",
                ["Authentication:Mode"] = "JwtAndApiKey",
                ["Authentication:DemoPassword"] = "Demo@RLogistics2026!",
                ["Authentication:ApiKeys:0:Name"] = "User demo key",
                ["Authentication:ApiKeys:0:Key"] = "rlogistics-demo-user-key-change-me",
                ["Authentication:ApiKeys:0:Email"] = "user@demo.local",
                ["Authentication:ApiKeys:1:Name"] = "Coordinator demo key",
                ["Authentication:ApiKeys:1:Key"] = "rlogistics-demo-coord-key-change-me",
                ["Authentication:ApiKeys:1:Email"] = "coord1@demo.local",
                ["Authentication:ApiKeys:2:Name"] = "Admin demo key",
                ["Authentication:ApiKeys:2:Key"] = "rlogistics-demo-admin-key-change-me",
                ["Authentication:ApiKeys:2:Email"] = "admin@demo.local",
            });
        });
    }

    public HttpClient CreateClientAsApiKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public const string UserKey = "rlogistics-demo-user-key-change-me";
    public const string CoordKey = "rlogistics-demo-coord-key-change-me";
    public const string AdminKey = "rlogistics-demo-admin-key-change-me";
}
