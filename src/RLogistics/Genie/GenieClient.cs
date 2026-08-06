using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RLogistics.Genie;

public sealed class GenieOptions
{
    public const string SectionName = "Genie";
    public string BaseUrl { get; set; } = "http://localhost:8090";
    public bool Enabled { get; set; } = true;
}

public interface IGenieClient
{
    Task<object?> GetIntakeAsync(int requestId, CancellationToken ct = default);
    Task<object?> GetCompletenessAsync(int requestId, CancellationToken ct = default);
    Task<object?> GetSummaryAsync(int requestId, CancellationToken ct = default);
    Task<object?> RecommendVendorsAsync(int requestId, CancellationToken ct = default);
    Task<object?> HealthAsync(CancellationToken ct = default);
}

public sealed class GenieClient(HttpClient http, IOptions<GenieOptions> options, ILogger<GenieClient> log) : IGenieClient
{
    private readonly GenieOptions _opts = options.Value;

    public async Task<object?> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<object>("/health", ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GENIE health failed");
            return new { ok = false, error = ex.Message };
        }
    }

    public Task<object?> GetIntakeAsync(int requestId, CancellationToken ct = default) =>
        Get($"/v1/intake/{requestId}", ct);

    public Task<object?> GetCompletenessAsync(int requestId, CancellationToken ct = default) =>
        Get($"/v1/completeness/{requestId}", ct);

    public Task<object?> GetSummaryAsync(int requestId, CancellationToken ct = default) =>
        Get($"/v1/summarize/{requestId}", ct);

    public Task<object?> RecommendVendorsAsync(int requestId, CancellationToken ct = default) =>
        Get($"/v1/vendors/recommend/{requestId}", ct);

    private async Task<object?> Get(string path, CancellationToken ct)
    {
        if (!_opts.Enabled) return new { error = "GENIE disabled" };
        try
        {
            return await http.GetFromJsonAsync<object>(path, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GENIE call failed {Path}", path);
            return new { error = ex.Message };
        }
    }
}
