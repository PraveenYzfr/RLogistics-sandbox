using RLogistics.Contracts;
using RLogistics.Domain;

namespace RLogistics.Abstractions;

public interface IRequestService
{
    Task<List<RequestSummaryDto>> ListAsync(RequestStatus? status, bool assignedToMeOnly, CancellationToken ct = default);
    Task<RequestDetailDto?> GetAsync(int id, CancellationToken ct = default);
    Task<RequestDetailDto> CreateAsync(CreateRequestDto dto, CancellationToken ct = default);
    Task<RequestDetailDto> AssignAsync(int id, AssignRequestDto dto, CancellationToken ct = default);
    Task<RequestDetailDto> UpdateStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default);
    Task<RequestDetailDto> AddClarificationAsync(int id, ClarificationDto dto, CancellationToken ct = default);
    Task<RequestDetailDto> ReplyClarificationAsync(int id, int clarificationId, string response, CancellationToken ct = default);
    Task<RequestDetailDto> UpdateFieldsAsync(int id, UpdateRequestFieldsDto dto, CancellationToken ct = default);
    Task<RequestDetailDto> PlanAsync(int id, PlanRequestDto dto, CancellationToken ct = default);
    Task<(int Sent, string Detail)> RequestVendorQuotesAsync(int id, CancellationToken ct = default);
    Task SendDeviceReturnReminderAsync(int id, CancellationToken ct = default);
    Task<int> RunOverdueDeviceReturnRemindersAsync(CancellationToken ct = default);
    Task<List<VendorDto>> ListVendorsAsync(VendorType? type = null, CancellationToken ct = default);
}

public interface IEmailNotificationService
{
    Task SendStatusChangeAsync(DisposalRequest request, RequestStatus from, RequestStatus to, CancellationToken ct = default);
    Task SendClarificationAsync(DisposalRequest request, string? question = null, CancellationToken ct = default);
    Task<(int Sent, string Detail)> SendVendorQuotesAsync(DisposalRequest request, CancellationToken ct = default);
    Task SendDeviceReturnReminderAsync(DisposalRequest request, CancellationToken ct = default);
    Task<int> SendOverdueDeviceReturnRemindersAsync(CancellationToken ct = default);
}

/// <summary>Email transport (Adapter target). Mock outbox vs future Graph.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string ToAddress,
    string Subject,
    string Body,
    int? RequestId,
    string? TemplateCode,
    string? StatusFrom,
    string? StatusTo);

/// <summary>Repository for disposal requests (data access isolation).</summary>
public interface IRequestRepository
{
    Task<DisposalRequest?> GetTrackedAsync(int id, CancellationToken ct = default);
    Task<DisposalRequest?> GetDetailAsync(int id, CancellationToken ct = default);
    IQueryable<DisposalRequest> Query();
    Task AddAsync(DisposalRequest entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<string> NextRequestNumberAsync(CancellationToken ct = default);
}

/// <summary>Builder product for entity construction from DTO.</summary>
public interface IDisposalRequestBuilder
{
    IDisposalRequestBuilder WithRequestor(AppUser requestor);
    IDisposalRequestBuilder WithContact(CreateRequestDto dto);
    IDisposalRequestBuilder WithFacility(CreateRequestDto dto);
    IDisposalRequestBuilder WithPickup(CreateRequestDto dto);
    IDisposalRequestBuilder WithAssets(IEnumerable<AssetDto> assets);
    IDisposalRequestBuilder WithDefaults(DateTime? preferredPickup, int defaultReturnDays);
    DisposalRequest Build(string requestNumber);
}

/// <summary>Facade for coordinator workflow orchestration entry points.</summary>
public interface IRequestWorkflowFacade
{
    Task<RequestDetailDto> ProcessClaimAsync(int id, CancellationToken ct = default);
    Task<RequestDetailDto> ProcessStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default);
    Task<(int Sent, string Detail)> ProcessQuotesAsync(int id, CancellationToken ct = default);
}
