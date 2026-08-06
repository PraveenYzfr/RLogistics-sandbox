using RLogistics.Genie;
using RLogistics.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RLogistics.Controllers;

[ApiController]
[Route("api/genie")]
[Authorize]
public class GenieProxyController(IGenieClient genie) : ControllerBase
{
    [HttpGet("health")]
    [AllowAnonymous]
    public Task<object?> Health(CancellationToken ct) => genie.HealthAsync(ct);

    [HttpGet("intake/{id:int}")]
    [Authorize(Policy = RLogisticsPermissions.RequestsRead)]
    public Task<object?> Intake(int id, CancellationToken ct) => genie.GetIntakeAsync(id, ct);

    [HttpGet("completeness/{id:int}")]
    [Authorize(Policy = RLogisticsPermissions.RequestsRead)]
    public Task<object?> Completeness(int id, CancellationToken ct) => genie.GetCompletenessAsync(id, ct);

    [HttpGet("summarize/{id:int}")]
    [Authorize(Policy = RLogisticsPermissions.RequestsRead)]
    public Task<object?> Summarize(int id, CancellationToken ct) => genie.GetSummaryAsync(id, ct);

    [HttpGet("vendors/recommend/{id:int}")]
    [Authorize(Policy = RLogisticsPermissions.RequestsPlan)]
    public Task<object?> Recommend(int id, CancellationToken ct) => genie.RecommendVendorsAsync(id, ct);
}
