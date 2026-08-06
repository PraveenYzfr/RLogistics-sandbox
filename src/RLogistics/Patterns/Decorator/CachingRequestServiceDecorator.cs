using RLogistics.Abstractions;
using RLogistics.Caching;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Services;

namespace RLogistics.Patterns.Decorator;

/// <summary>
/// Cache-aside decorator for hot read paths (details + vendors). Writes invalidate related keys.
/// </summary>
public sealed class CachingRequestServiceDecorator(
    IRequestService inner,
    ICacheService cache,
    ILogger<CachingRequestServiceDecorator> log) : IRequestService
{
    private static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan VendorTtl = TimeSpan.FromMinutes(5);

    public Task<List<RequestSummaryDto>> ListAsync(RequestStatus? status, bool assignedToMeOnly, CancellationToken ct = default) =>
        inner.ListAsync(status, assignedToMeOnly, ct);

    public async Task<RequestDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var key = CacheKeys.Request(id);
        var hit = await cache.GetAsync<RequestDetailDto>(key, ct);
        if (hit is not null)
        {
            log.LogDebug("Cache hit {Key}", key);
            return hit;
        }

        var item = await inner.GetAsync(id, ct);
        if (item is not null)
            await cache.SetAsync(key, item, DetailTtl, ct);
        return item;
    }

    public async Task<RequestDetailDto> CreateAsync(CreateRequestDto dto, CancellationToken ct = default)
    {
        var created = await inner.CreateAsync(dto, ct);
        await cache.RemoveAsync(CacheKeys.Request(created.Id), ct);
        return created;
    }

    public async Task<RequestDetailDto> AssignAsync(int id, AssignRequestDto dto, CancellationToken ct = default)
    {
        var r = await inner.AssignAsync(id, dto, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<RequestDetailDto> UpdateStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default)
    {
        var r = await inner.UpdateStatusAsync(id, dto, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<RequestDetailDto> AddClarificationAsync(int id, ClarificationDto dto, CancellationToken ct = default)
    {
        var r = await inner.AddClarificationAsync(id, dto, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<RequestDetailDto> ReplyClarificationAsync(int id, int clarificationId, string response, CancellationToken ct = default)
    {
        var r = await inner.ReplyClarificationAsync(id, clarificationId, response, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<RequestDetailDto> UpdateFieldsAsync(int id, UpdateRequestFieldsDto dto, CancellationToken ct = default)
    {
        var r = await inner.UpdateFieldsAsync(id, dto, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<RequestDetailDto> PlanAsync(int id, PlanRequestDto dto, CancellationToken ct = default)
    {
        var r = await inner.PlanAsync(id, dto, ct);
        await InvalidateRequest(id, ct);
        return r;
    }

    public async Task<(int Sent, string Detail)> RequestVendorQuotesAsync(int id, CancellationToken ct = default)
    {
        var result = await inner.RequestVendorQuotesAsync(id, ct);
        await InvalidateRequest(id, ct);
        return result;
    }

    public async Task SendDeviceReturnReminderAsync(int id, CancellationToken ct = default)
    {
        await inner.SendDeviceReturnReminderAsync(id, ct);
        await InvalidateRequest(id, ct);
    }

    public Task<int> RunOverdueDeviceReturnRemindersAsync(CancellationToken ct = default) =>
        inner.RunOverdueDeviceReturnRemindersAsync(ct);

    public async Task<List<VendorDto>> ListVendorsAsync(VendorType? type = null, CancellationToken ct = default)
    {
        var key = CacheKeys.Vendors(type?.ToString());
        var hit = await cache.GetAsync<List<VendorDto>>(key, ct);
        if (hit is not null) return hit;

        var list = await inner.ListVendorsAsync(type, ct);
        await cache.SetAsync(key, list, VendorTtl, ct);
        return list;
    }

    private Task InvalidateRequest(int id, CancellationToken ct) =>
        cache.RemoveAsync(CacheKeys.Request(id), ct);
}
