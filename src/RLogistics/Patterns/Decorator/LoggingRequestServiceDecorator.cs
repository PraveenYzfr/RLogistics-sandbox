using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;

namespace RLogistics.Patterns.Decorator;

/// <summary>
/// Decorator — wraps IRequestService with structured audit logging without changing core logic.
/// </summary>
public sealed class LoggingRequestServiceDecorator(
    IRequestService inner,
    ILogger<LoggingRequestServiceDecorator> log,
    PersonaContext persona) : IRequestService
{
    public Task<List<RequestSummaryDto>> ListAsync(RequestStatus? status, bool assignedToMeOnly, CancellationToken ct = default)
    {
        log.LogInformation("List requests by {User} status={Status} mine={Mine}",
            persona.Current?.Email, status, assignedToMeOnly);
        return inner.ListAsync(status, assignedToMeOnly, ct);
    }

    public Task<RequestDetailDto?> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);

    public async Task<RequestDetailDto> CreateAsync(CreateRequestDto dto, CancellationToken ct = default)
    {
        log.LogInformation("Create request by {User} site={Site}", persona.Current?.Email, dto.Site);
        return await inner.CreateAsync(dto, ct);
    }

    public Task<RequestDetailDto> AssignAsync(int id, AssignRequestDto dto, CancellationToken ct = default) =>
        inner.AssignAsync(id, dto, ct);

    public async Task<RequestDetailDto> UpdateStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default)
    {
        log.LogInformation("Status change request {Id} → {Status} by {User}", id, dto.Status, persona.Current?.Email);
        return await inner.UpdateStatusAsync(id, dto, ct);
    }

    public Task<RequestDetailDto> AddClarificationAsync(int id, ClarificationDto dto, CancellationToken ct = default) =>
        inner.AddClarificationAsync(id, dto, ct);

    public Task<RequestDetailDto> ReplyClarificationAsync(int id, int clarificationId, string response, CancellationToken ct = default) =>
        inner.ReplyClarificationAsync(id, clarificationId, response, ct);

    public Task<RequestDetailDto> UpdateFieldsAsync(int id, UpdateRequestFieldsDto dto, CancellationToken ct = default) =>
        inner.UpdateFieldsAsync(id, dto, ct);

    public Task<RequestDetailDto> PlanAsync(int id, PlanRequestDto dto, CancellationToken ct = default) =>
        inner.PlanAsync(id, dto, ct);

    public async Task<(int Sent, string Detail)> RequestVendorQuotesAsync(int id, CancellationToken ct = default)
    {
        log.LogInformation("Vendor quotes for request {Id}", id);
        return await inner.RequestVendorQuotesAsync(id, ct);
    }

    public Task SendDeviceReturnReminderAsync(int id, CancellationToken ct = default) =>
        inner.SendDeviceReturnReminderAsync(id, ct);

    public Task<int> RunOverdueDeviceReturnRemindersAsync(CancellationToken ct = default) =>
        inner.RunOverdueDeviceReturnRemindersAsync(ct);

    public Task<List<VendorDto>> ListVendorsAsync(VendorType? type = null, CancellationToken ct = default) =>
        inner.ListVendorsAsync(type, ct);
}
