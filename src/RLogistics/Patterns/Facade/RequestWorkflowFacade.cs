using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;

namespace RLogistics.Patterns.Facade;

/// <summary>
/// Facade — simplified surface for common coordinator orchestration paths.
/// Controllers/pages can call a small API instead of many low-level service methods.
/// </summary>
public sealed class RequestWorkflowFacade(IRequestService requests) : IRequestWorkflowFacade
{
    public Task<RequestDetailDto> ProcessClaimAsync(int id, CancellationToken ct = default) =>
        requests.AssignAsync(id, new AssignRequestDto(null), ct);

    public Task<RequestDetailDto> ProcessStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default) =>
        requests.UpdateStatusAsync(id, dto, ct);

    public Task<(int Sent, string Detail)> ProcessQuotesAsync(int id, CancellationToken ct = default) =>
        requests.RequestVendorQuotesAsync(id, ct);
}
