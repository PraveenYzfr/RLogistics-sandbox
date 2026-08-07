using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    Task<object?> GetEvalCasesAsync(bool? pendingSme = true, CancellationToken ct = default);
    Task<object?> GetEvalMetricsAsync(CancellationToken ct = default);
    Task<object?> SubmitSmeScoreAsync(string caseId, double score, bool passed, string notes, string reviewer, CancellationToken ct = default);
    Task<object?> GetObservabilitySummaryAsync(CancellationToken ct = default);
    Task<object?> RunAgentsAsync(int requestId, CancellationToken ct = default);
    Task<object?> GetLlmStatusAsync(CancellationToken ct = default);
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

    public Task<object?> GetEvalCasesAsync(bool? pendingSme = true, CancellationToken ct = default)
    {
        var q = pendingSme is null ? "" : $"?pending_sme={pendingSme.Value.ToString().ToLowerInvariant()}";
        return Get($"/v1/eval/cases{q}", ct);
    }

    public Task<object?> GetEvalMetricsAsync(CancellationToken ct = default) =>
        Get("/v1/eval/metrics", ct);

    public Task<object?> GetObservabilitySummaryAsync(CancellationToken ct = default) =>
        Get("/v1/observability/summary?day=today", ct);

    public Task<object?> GetLlmStatusAsync(CancellationToken ct = default) =>
        Get("/v1/llm/status", ct);

    public async Task<object?> RunAgentsAsync(int requestId, CancellationToken ct = default)
    {
        if (!_opts.Enabled) return new { error = "GENIE disabled" };
        try
        {
            var resp = await http.PostAsync($"/v1/agents/run/{requestId}", null, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new { error = $"HTTP {(int)resp.StatusCode}", body };
            return System.Text.Json.JsonSerializer.Deserialize<object>(body);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GENIE agent run failed {Id}", requestId);
            return new { error = ex.Message };
        }
    }

    public async Task<object?> SubmitSmeScoreAsync(
        string caseId,
        double score,
        bool passed,
        string notes,
        string reviewer,
        CancellationToken ct = default)
    {
        if (!_opts.Enabled) return new { error = "GENIE disabled" };
        try
        {
            var payload = new
            {
                score_0_to_5 = score,
                passed,
                notes,
                reviewer
            };
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            var resp = await http.PostAsync($"/v1/eval/cases/{caseId}/sme", content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new { error = $"HTTP {(int)resp.StatusCode}", body };
            return JsonSerializer.Deserialize<object>(body);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GENIE SME submit failed {CaseId}", caseId);
            return new { error = ex.Message };
        }
    }

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
