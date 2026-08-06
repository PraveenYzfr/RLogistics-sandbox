using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Patterns.Repository;

/// <summary>
/// Repository pattern — isolates EF Core from application services.
/// </summary>
public sealed class RequestRepository(RLogisticsDbContext db) : IRequestRepository
{
    public IQueryable<DisposalRequest> Query() =>
        db.Requests.AsNoTracking()
            .Include(r => r.Requestor)
            .Include(r => r.AssignedCoordinator)
            .Include(r => r.TransportVendor)
            .Include(r => r.ProcessingVendor)
            .Include(r => r.Assets);

    public async Task<DisposalRequest?> GetDetailAsync(int id, CancellationToken ct = default) =>
        await db.Requests.AsNoTracking()
            .Include(x => x.Requestor)
            .Include(x => x.AssignedCoordinator)
            .Include(x => x.TransportVendor)
            .Include(x => x.ProcessingVendor)
            .Include(x => x.Assets)
            .Include(x => x.AuditLogs)
            .Include(x => x.Clarifications)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<DisposalRequest?> GetTrackedAsync(int id, CancellationToken ct = default) =>
        await db.Requests
            .Include(r => r.Assets)
            .Include(r => r.AuditLogs)
            .Include(r => r.Clarifications)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(DisposalRequest entity, CancellationToken ct = default) =>
        await db.Requests.AddAsync(entity, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<string> NextRequestNumberAsync(CancellationToken ct = default)
    {
        var last = await db.Requests.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Select(r => r.RequestNumber)
            .FirstOrDefaultAsync(ct);

        var n = 1000;
        if (last is not null && last.StartsWith("RLogistics-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(last[4..], out var parsed))
            n = parsed;

        return $"RLogistics-{n + 1}";
    }
}
