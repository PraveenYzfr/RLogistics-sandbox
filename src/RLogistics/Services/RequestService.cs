using RLogistics.Abstractions;
using RLogistics.Contracts;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Patterns.Builder;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Services;

public class RequestService(
    RLogisticsDbContext db,
    PersonaContext persona,
    IEmailNotificationService email,
    IDisposalRequestBuilderFactory requestBuilderFactory) : IRequestService
{
    public async Task<List<RequestSummaryDto>> ListAsync(RequestStatus? status, bool assignedToMeOnly, CancellationToken ct = default)
    {
        var me = persona.Require();
        var q = db.Requests.AsNoTracking()
            .Include(r => r.Requestor)
            .Include(r => r.AssignedCoordinator)
            .Include(r => r.TransportVendor)
            .Include(r => r.ProcessingVendor)
            .Include(r => r.Assets)
            .AsQueryable();

        if (me.Role == UserRole.User)
            q = q.Where(r => r.RequestorUserId == me.Id);
        else if (assignedToMeOnly)
            q = q.Where(r => r.AssignedCoordinatorUserId == me.Id);

        if (status is not null)
            q = q.Where(r => r.Status == status);

        var rows = await q.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<RequestDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var me = persona.Require();
        var r = await db.Requests.AsNoTracking()
            .Include(x => x.Requestor)
            .Include(x => x.AssignedCoordinator)
            .Include(x => x.TransportVendor)
            .Include(x => x.ProcessingVendor)
            .Include(x => x.Assets)
            .Include(x => x.AuditLogs)
            .Include(x => x.Clarifications)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r is null) return null;
        EnsureCanView(me, r);
        return ToDetail(r);
    }

    public async Task<RequestDetailDto> CreateAsync(CreateRequestDto dto, CancellationToken ct = default)
    {
        var me = persona.Current;
        AppUser requestor;

        if (!string.IsNullOrWhiteSpace(dto.RequestorEmail))
        {
            requestor = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.RequestorEmail, ct)
                ?? throw new InvalidOperationException($"Unknown requestor email: {dto.RequestorEmail}");
            // Partner/API create may not have persona; default to requestor or require admin/coord
            me ??= requestor;
        }
        else
        {
            me = persona.Require();
            requestor = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        }

        if (dto.Assets is null || dto.Assets.Count == 0)
            throw new InvalidOperationException("At least one asset is required.");
        if (string.IsNullOrWhiteSpace(dto.ContactName) || string.IsNullOrWhiteSpace(dto.ContactEmail))
            throw new InvalidOperationException("Contact name and email are required.");
        if (string.IsNullOrWhiteSpace(dto.Site))
            throw new InvalidOperationException("Facility / site name is required.");
        if (string.IsNullOrWhiteSpace(dto.PickupAddressLine1) || string.IsNullOrWhiteSpace(dto.PickupCity))
            throw new InvalidOperationException("Pickup address and city are required.");

        var number = await NextRequestNumberAsync(ct);
        var defaultReturnDays = await GetConfigIntAsync("DefaultDeviceReturnDays", 7, ct);
        var entity = requestBuilderFactory.Create()
            .WithRequestor(requestor)
            .WithContact(dto)
            .WithFacility(dto)
            .WithPickup(dto)
            .WithAssets(dto.Assets)
            .WithDefaults(dto.PreferredPickupDate, defaultReturnDays)
            .Build(number);

        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "Created",
            Detail = $"Created by {me.Email}"
        });

        db.Requests.Add(entity);
        await db.SaveChangesAsync(ct);

        // reload for email
        entity.Requestor = requestor;
        await email.SendStatusChangeAsync(entity, RequestStatus.Created, RequestStatus.Created, ct);

        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<RequestDetailDto> AssignAsync(int id, AssignRequestDto dto, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);

        var entity = await LoadTrackedAsync(id, ct);
        var coordinatorId = dto.CoordinatorUserId ?? me.Id;
        var coordinator = await db.Users.FirstOrDefaultAsync(u => u.Id == coordinatorId, ct)
            ?? throw new InvalidOperationException("Coordinator not found.");
        if (coordinator.Role is not (UserRole.Coordinator or UserRole.Admin))
            throw new InvalidOperationException("Assignee must be a coordinator or admin.");

        var from = entity.Status;
        entity.AssignedCoordinatorUserId = coordinatorId;
        if (entity.Status == RequestStatus.Created)
            entity.Status = RequestStatus.Assigned;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "Assigned",
            Detail = $"Assigned to {coordinator.Email}"
        });
        await db.SaveChangesAsync(ct);

        if (from != entity.Status)
        {
            await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
            await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
            await email.SendStatusChangeAsync(entity, from, entity.Status, ct);
        }

        return (await GetAsync(id, ct))!;
    }

    public async Task<RequestDetailDto> UpdateStatusAsync(int id, UpdateStatusDto dto, CancellationToken ct = default)
    {
        var me = persona.Require();
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        var from = entity.Status;
        if (from == dto.Status && string.IsNullOrWhiteSpace(dto.Notes))
            return (await GetAsync(id, ct))!;

        entity.Status = dto.Status;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
            entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "StatusChanged",
            Detail = $"{from} → {dto.Status}"
        });
        await db.SaveChangesAsync(ct);

        await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.TransportVendor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.ProcessingVendor).LoadAsync(ct);
        await email.SendStatusChangeAsync(entity, from, dto.Status, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<RequestDetailDto> AddClarificationAsync(int id, ClarificationDto dto, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        var from = entity.Status;
        entity.Clarifications.Add(new Clarification
        {
            ActorUserId = me.Id,
            Question = dto.Question.Trim()
        });
        entity.Status = RequestStatus.OnHold;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "ClarificationRequested",
            Detail = dto.Question.Trim()
        });
        await db.SaveChangesAsync(ct);

        await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
        await email.SendClarificationAsync(entity, dto.Question.Trim(), ct);
        if (from != entity.Status)
            await email.SendStatusChangeAsync(entity, from, entity.Status, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<RequestDetailDto> ReplyClarificationAsync(int id, int clarificationId, string response, CancellationToken ct = default)
    {
        var me = persona.Require();
        var entity = await LoadTrackedAsync(id, ct);
        if (me.Role == UserRole.User && entity.RequestorUserId != me.Id)
            throw new UnauthorizedAccessException("Users can only reply on their own requests.");

        var c = entity.Clarifications.FirstOrDefault(x => x.Id == clarificationId)
            ?? throw new KeyNotFoundException("Clarification not found.");
        if (!string.IsNullOrWhiteSpace(c.Response))
            throw new InvalidOperationException("Already answered.");

        c.Response = response.Trim();
        var from = entity.Status;
        if (entity.Status == RequestStatus.OnHold)
            entity.Status = RequestStatus.Assigned;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "ClarificationAnswered",
            Detail = response.Trim()
        });
        await db.SaveChangesAsync(ct);

        if (from != entity.Status)
        {
            await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
            await email.SendStatusChangeAsync(entity, from, entity.Status, ct);
        }

        return (await GetAsync(id, ct))!;
    }

    public async Task<RequestDetailDto> UpdateFieldsAsync(int id, UpdateRequestFieldsDto dto, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        if (dto.Notes is not null) entity.Notes = dto.Notes;
        if (dto.CoordinatorNotes is not null) entity.CoordinatorNotes = dto.CoordinatorNotes;
        if (dto.PickupInstructions is not null) entity.PickupInstructions = dto.PickupInstructions;
        entity.UpdatedAt = DateTime.UtcNow;
        if (entity.Status == RequestStatus.Created)
            entity.Status = RequestStatus.Assigned;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "Updated",
            Detail = "Coordinator updated request notes / instructions"
        });
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<RequestDetailDto> PlanAsync(int id, PlanRequestDto dto, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        if (dto.TransportVendorId is int tid)
        {
            var v = await db.Vendors.FirstOrDefaultAsync(x => x.Id == tid && x.Type == VendorType.Transport && x.IsActive, ct)
                ?? throw new InvalidOperationException("Invalid transport vendor.");
            entity.TransportVendorId = v.Id;
        }
        if (dto.ProcessingVendorId is int pid)
        {
            var v = await db.Vendors.FirstOrDefaultAsync(x => x.Id == pid && x.Type == VendorType.Processing && x.IsActive, ct)
                ?? throw new InvalidOperationException("Invalid processing vendor.");
            entity.ProcessingVendorId = v.Id;
        }

        if (dto.ScheduledPickupDate is not null)
            entity.ScheduledPickupDate = dto.ScheduledPickupDate;
        if (dto.ScheduledPickupSlot is not null)
            entity.ScheduledPickupSlot = string.IsNullOrWhiteSpace(dto.ScheduledPickupSlot) ? null : dto.ScheduledPickupSlot;
        if (dto.ExpectedDeviceReturnDate is not null)
            entity.ExpectedDeviceReturnDate = dto.ExpectedDeviceReturnDate.Value.Date;

        var from = entity.Status;
        if (dto.MarkPickupScheduled &&
            entity.ScheduledPickupDate is not null &&
            !string.IsNullOrWhiteSpace(entity.ScheduledPickupSlot))
        {
            entity.Status = RequestStatus.PickupScheduled;
        }
        else if (entity.Status is RequestStatus.Created)
        {
            entity.Status = RequestStatus.Assigned;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "PlanUpdated",
            Detail = $"Transport={entity.TransportVendorId}, Processing={entity.ProcessingVendorId}, Pickup={entity.ScheduledPickupDate:d} {entity.ScheduledPickupSlot}, ReturnBy={entity.ExpectedDeviceReturnDate:d}"
        });
        await db.SaveChangesAsync(ct);

        if (from != entity.Status)
        {
            await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
            await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
            await db.Entry(entity).Reference(x => x.TransportVendor).LoadAsync(ct);
            await db.Entry(entity).Reference(x => x.ProcessingVendor).LoadAsync(ct);
            await email.SendStatusChangeAsync(entity, from, entity.Status, ct);
        }

        return (await GetAsync(id, ct))!;
    }

    public async Task<(int Sent, string Detail)> RequestVendorQuotesAsync(int id, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        if (entity.TransportVendorId is null && entity.ProcessingVendorId is null)
            throw new InvalidOperationException("Select a transport and/or processing vendor first, then save plan.");

        await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.TransportVendor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.ProcessingVendor).LoadAsync(ct);

        var result = await email.SendVendorQuotesAsync(entity, ct);
        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "VendorQuotesSent",
            Detail = result.Detail
        });
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task SendDeviceReturnReminderAsync(int id, CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        var entity = await LoadTrackedAsync(id, ct);
        EnsureCanWork(me, entity);

        await db.Entry(entity).Reference(x => x.Requestor).LoadAsync(ct);
        await db.Entry(entity).Reference(x => x.AssignedCoordinator).LoadAsync(ct);
        await email.SendDeviceReturnReminderAsync(entity, ct);

        entity.AuditLogs.Add(new AuditLog
        {
            ActorUserId = me.Id,
            Action = "DeviceReturnReminderSent",
            Detail = $"Expected return {entity.ExpectedDeviceReturnDate:d}"
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RunOverdueDeviceReturnRemindersAsync(CancellationToken ct = default)
    {
        var me = persona.Require();
        EnsureCoordinator(me);
        return await email.SendOverdueDeviceReturnRemindersAsync(ct);
    }

    public async Task<List<VendorDto>> ListVendorsAsync(VendorType? type = null, CancellationToken ct = default)
    {
        var q = db.Vendors.AsNoTracking().Where(v => v.IsActive);
        if (type is not null) q = q.Where(v => v.Type == type);
        return await q.OrderBy(v => v.Name)
            .Select(v => new VendorDto(v.Id, v.Name, v.Type, v.ServiceArea, v.Email))
            .ToListAsync(ct);
    }

    private async Task<int> GetConfigIntAsync(string key, int defaultValue, CancellationToken ct)
    {
        var row = await db.AppConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
        return row is not null && int.TryParse(row.Value, out var n) ? n : defaultValue;
    }
    private async Task<DisposalRequest> LoadTrackedAsync(int id, CancellationToken ct) =>
        await db.Requests
            .Include(r => r.Assets)
            .Include(r => r.AuditLogs)
            .Include(r => r.Clarifications)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new KeyNotFoundException($"Request {id} not found.");

    private async Task<string> NextRequestNumberAsync(CancellationToken ct)
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

    private static void EnsureCoordinator(AppUser me)
    {
        if (me.Role is not (UserRole.Coordinator or UserRole.Admin))
            throw new UnauthorizedAccessException("Coordinator or Admin role required.");
    }

    private static void EnsureCanView(AppUser me, DisposalRequest r)
    {
        if (me.Role == UserRole.User && r.RequestorUserId != me.Id)
            throw new UnauthorizedAccessException("Users can only view their own requests.");
    }

    private static void EnsureCanWork(AppUser me, DisposalRequest r)
    {
        if (me.Role == UserRole.User)
        {
            if (r.RequestorUserId != me.Id)
                throw new UnauthorizedAccessException("Users can only update their own requests.");
            // Users may only respond path: allow limited status? For Phase 0 users don't change status except via clarifications reply later
            throw new UnauthorizedAccessException("Users cannot change request status. Contact a coordinator.");
        }
    }

    private static RequestSummaryDto ToSummary(DisposalRequest r) => new(
        r.Id,
        r.RequestNumber,
        r.Site,
        r.PickupCity,
        r.DispositionType,
        r.RequestType,
        r.Status,
        r.Requestor.Email,
        r.ContactName,
        r.AssignedCoordinator?.Email,
        r.CreatedAt,
        r.Assets.Count,
        r.PreferredPickupDate,
        r.ScheduledPickupDate,
        r.TransportVendor?.Name,
        r.ProcessingVendor?.Name);

    private static RequestDetailDto ToDetail(DisposalRequest r) => new(
        r.Id,
        r.RequestNumber,
        r.Site,
        r.FacilityCode,
        r.Building,
        r.Floor,
        r.Room,
        r.ContactName,
        r.ContactEmail,
        r.ContactPhone,
        r.ContactDepartment,
        r.PickupAddressLine1,
        r.PickupAddressLine2,
        r.PickupCity,
        r.PickupState,
        r.PickupPostalCode,
        r.PickupCountry,
        r.PreferredPickupDate,
        r.PickupInstructions,
        r.DispositionType,
        r.RequestType,
        r.Status,
        r.Requestor.Email,
        r.RequestorUserId,
        r.AssignedCoordinator?.Email,
        r.AssignedCoordinatorUserId,
        r.Notes,
        r.CoordinatorNotes,
        r.TransportVendorId,
        r.TransportVendor?.Name,
        r.ProcessingVendorId,
        r.ProcessingVendor?.Name,
        r.ScheduledPickupDate,
        r.ScheduledPickupSlot,
        r.ExpectedDeviceReturnDate,
        r.LastReturnReminderAt,
        r.CreatedAt,
        r.Assets.Select(a => new AssetDto(a.AssetType, a.SerialNumber, a.Quantity, a.Manufacturer, a.Model, a.AssetTag, a.Condition, a.DeviceGuid)).ToList(),
        r.AuditLogs.OrderByDescending(a => a.At).Select(a => new AuditItemDto(a.Action, a.Detail, a.At, a.ActorUserId)).ToList(),
        r.Clarifications.OrderByDescending(c => c.CreatedAt).Select(c => new ClarificationItemDto(c.Id, c.Question, c.Response, c.CreatedAt)).ToList());
}
