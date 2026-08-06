using FluentAssertions;
using RLogistics.Abstractions;
using RLogistics.Caching;
using RLogistics.Contracts;
using RLogistics.Domain;
using RLogistics.Patterns.Decorator;
using Microsoft.Extensions.Logging.Abstractions;

namespace RLogistics.Tests.Unit;

public class CachingDecoratorTests
{
    [Fact]
    public async Task Cache_hit_skips_inner_second_get()
    {
        var calls = 0;
        var detail = SampleDetail();
        var inner = new CountingRequestService(() => { calls++; return detail; });
        var store = new DictCache();
        var decorator = new CachingRequestServiceDecorator(
            inner, store, NullLogger<CachingRequestServiceDecorator>.Instance);

        var a = await decorator.GetAsync(1);
        var b = await decorator.GetAsync(1);

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        calls.Should().Be(1);
    }

    private static RequestDetailDto SampleDetail() => new(
        Id: 1,
        RequestNumber: "RLogistics-1",
        Site: "Site",
        FacilityCode: null,
        Building: null,
        Floor: null,
        Room: null,
        ContactName: "Alex",
        ContactEmail: "a@b.com",
        ContactPhone: null,
        ContactDepartment: null,
        PickupAddressLine1: "1 St",
        PickupAddressLine2: null,
        PickupCity: "City",
        PickupState: null,
        PickupPostalCode: null,
        PickupCountry: "USA",
        PreferredPickupDate: null,
        PickupInstructions: null,
        DispositionType: DispositionType.Sanitize,
        RequestType: RequestType.UsSurplus,
        Status: RequestStatus.Created,
        RequestorEmail: "user@demo.local",
        RequestorUserId: 1,
        AssignedCoordinatorEmail: null,
        AssignedCoordinatorUserId: null,
        Notes: null,
        CoordinatorNotes: null,
        TransportVendorId: null,
        TransportVendorName: null,
        ProcessingVendorId: null,
        ProcessingVendorName: null,
        ScheduledPickupDate: null,
        ScheduledPickupSlot: null,
        ExpectedDeviceReturnDate: null,
        LastReturnReminderAt: null,
        CreatedAt: DateTime.UtcNow,
        Assets: [],
        Audit: [],
        Clarifications: []);

    private sealed class CountingRequestService(Func<RequestDetailDto> factory) : IRequestService
    {
        public Task<RequestDetailDto?> GetAsync(int id, CancellationToken ct = default) =>
            Task.FromResult<RequestDetailDto?>(factory());

        public Task<List<RequestSummaryDto>> ListAsync(RequestStatus? status, bool assignedToMeOnly, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<RequestDetailDto> CreateAsync(CreateRequestDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> AssignAsync(int id, AssignRequestDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> UpdateStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> AddClarificationAsync(int id, ClarificationDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> ReplyClarificationAsync(int id, int clarificationId, string response, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> UpdateFieldsAsync(int id, UpdateRequestFieldsDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RequestDetailDto> PlanAsync(int id, PlanRequestDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(int Sent, string Detail)> RequestVendorQuotesAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SendDeviceReturnReminderAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> RunOverdueDeviceReturnRemindersAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<VendorDto>> ListVendorsAsync(VendorType? type = null, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class DictCache : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out var o) ? (T?)o : default);
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
    }
}
