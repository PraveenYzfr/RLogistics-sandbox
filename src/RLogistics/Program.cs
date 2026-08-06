using RLogistics;
using RLogistics.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRLogistics(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RLogisticsDbContext>();
    await DbSeeder.SeedAsync(db);
}

app.UseRLogisticsPipeline();
app.Run();

/// <summary>Exposes entry assembly for WebApplicationFactory integration tests.</summary>
public partial class Program;
