using RLogistics.Contracts;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Abstractions;
using RLogistics.Security;
using RLogistics.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(RLogisticsDbContext db) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous] // persona discovery + login UX; production would lock this
    public async Task<ActionResult<List<UserDto>>> GetAll(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Role).ThenBy(u => u.DisplayName)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName, u.Role))
            .ToListAsync(ct);
        return users;
    }
}

[ApiController]
[Route("api/requests")]
[Authorize]
public class RequestsController(IRequestService requests) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RLogisticsPermissions.RequestsRead)]
    public async Task<ActionResult<List<RequestSummaryDto>>> List(
        [FromQuery] RequestStatus? status,
        [FromQuery] bool assignedToMe = false,
        CancellationToken ct = default)
        => await requests.ListAsync(status, assignedToMe, ct);

    [HttpGet("{id:int}")]
    [Authorize(Policy = RLogisticsPermissions.RequestsRead)]
    public async Task<ActionResult<RequestDetailDto>> Get(int id, CancellationToken ct)
    {
        var item = await requests.GetAsync(id, ct);
        return item is null ? NotFound() : item;
    }

    [HttpPost]
    [Authorize(Policy = RLogisticsPermissions.RequestsWrite)]
    public async Task<ActionResult<RequestDetailDto>> Create([FromBody] CreateRequestDto dto, CancellationToken ct)
    {
        var created = await requests.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/assign")]
    [Authorize(Policy = RLogisticsPermissions.RequestsAssign)]
    public Task<RequestDetailDto> Assign(int id, [FromBody] AssignRequestDto dto, CancellationToken ct)
        => requests.AssignAsync(id, dto, ct);

    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = RLogisticsPermissions.RequestsStatus)]
    public Task<RequestDetailDto> UpdateStatus(int id, [FromBody] UpdateStatusDto dto, CancellationToken ct)
        => requests.UpdateStatusAsync(id, dto, ct);

    [HttpPost("{id:int}/clarifications")]
    [Authorize(Policy = RLogisticsPermissions.RequestsClarify)]
    public Task<RequestDetailDto> Clarify(int id, [FromBody] ClarificationDto dto, CancellationToken ct)
        => requests.AddClarificationAsync(id, dto, ct);

    [HttpPost("{id:int}/clarifications/{clarificationId:int}/reply")]
    [Authorize(Policy = RLogisticsPermissions.RequestsWrite)]
    public Task<RequestDetailDto> ReplyClarification(
        int id, int clarificationId, [FromBody] ClarificationReplyDto dto, CancellationToken ct)
        => requests.ReplyClarificationAsync(id, clarificationId, dto.Response, ct);

    [HttpPatch("{id:int}/fields")]
    [Authorize(Policy = RLogisticsPermissions.RequestsWrite)]
    public Task<RequestDetailDto> UpdateFields(int id, [FromBody] UpdateRequestFieldsDto dto, CancellationToken ct)
        => requests.UpdateFieldsAsync(id, dto, ct);

    [HttpPost("{id:int}/plan")]
    [Authorize(Policy = RLogisticsPermissions.RequestsPlan)]
    public Task<RequestDetailDto> Plan(int id, [FromBody] PlanRequestDto dto, CancellationToken ct)
        => requests.PlanAsync(id, dto, ct);

    [HttpPost("{id:int}/vendor-quotes")]
    [Authorize(Policy = RLogisticsPermissions.RequestsQuotes)]
    public async Task<ActionResult<object>> VendorQuotes(int id, CancellationToken ct)
    {
        var result = await requests.RequestVendorQuotesAsync(id, ct);
        return Ok(new { sent = result.Sent, detail = result.Detail });
    }

    [HttpPost("{id:int}/return-reminder")]
    [Authorize(Policy = RLogisticsPermissions.RequestsReminders)]
    public async Task<IActionResult> ReturnReminder(int id, CancellationToken ct)
    {
        await requests.SendDeviceReturnReminderAsync(id, ct);
        return Ok(new { ok = true });
    }
}

[ApiController]
[Route("api/vendors")]
[Authorize(Policy = RLogisticsPermissions.VendorsRead)]
public class VendorsController(IRequestService requests) : ControllerBase
{
    [HttpGet]
    public Task<List<VendorDto>> List([FromQuery] VendorType? type, CancellationToken ct)
        => requests.ListVendorsAsync(type, ct);
}

[ApiController]
[Route("api/email-outbox")]
[Authorize]
public class EmailOutboxController(RLogisticsDbContext db, PersonaContext persona, IRequestService requests) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RLogisticsPermissions.EmailOutboxRead)]
    public async Task<ActionResult<List<EmailOutboxDto>>> List(CancellationToken ct)
    {
        var me = persona.Current;
        if (me is null) return Unauthorized(new { error = "Not authenticated." });

        var q = db.EmailOutbox.AsNoTracking().AsQueryable();
        if (me.Role == UserRole.User)
            q = q.Where(e => e.ToAddress == me.Email);

        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(200)
            .Select(e => new EmailOutboxDto(e.Id, e.ToAddress, e.Subject, e.Body, e.RequestId, e.TemplateCode, e.StatusFrom, e.StatusTo, e.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }

    [HttpPost("run-return-reminders")]
    [Authorize(Policy = RLogisticsPermissions.EmailRemindersRun)]
    public async Task<ActionResult<object>> RunReturnReminders(CancellationToken ct)
    {
        var count = await requests.RunOverdueDeviceReturnRemindersAsync(ct);
        return Ok(new { remindersSent = count });
    }
}

[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AdminController(RLogisticsDbContext db) : ControllerBase
{
    [HttpGet("email-templates")]
    [Authorize(Policy = RLogisticsPermissions.AdminTemplates)]
    public async Task<ActionResult<List<EmailTemplate>>> GetTemplates(CancellationToken ct)
        => await db.EmailTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct);

    [HttpPut("email-templates/{code}")]
    [Authorize(Policy = RLogisticsPermissions.AdminTemplates)]
    public async Task<ActionResult<EmailTemplate>> UpsertTemplate(string code, [FromBody] EmailTemplateDto dto, CancellationToken ct)
    {
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (t is null)
        {
            t = new EmailTemplate { Code = code };
            db.EmailTemplates.Add(t);
        }
        t.SubjectTemplate = dto.SubjectTemplate;
        t.BodyTemplate = dto.BodyTemplate;
        t.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return t;
    }

    [HttpGet("config")]
    [Authorize(Policy = RLogisticsPermissions.AdminConfig)]
    public async Task<ActionResult<List<AppConfig>>> GetConfig(CancellationToken ct)
        => await db.AppConfigs.AsNoTracking().OrderBy(c => c.Key).ToListAsync(ct);

    [HttpPut("config/{key}")]
    [Authorize(Policy = RLogisticsPermissions.AdminConfig)]
    public async Task<ActionResult<AppConfig>> UpsertConfig(string key, [FromBody] AppConfigDto dto, CancellationToken ct)
    {
        var c = await db.AppConfigs.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (c is null)
        {
            c = new AppConfig { Key = key };
            db.AppConfigs.Add(c);
        }
        c.Value = dto.Value;
        c.Description = dto.Description ?? c.Description;
        await db.SaveChangesAsync(ct);
        return c;
    }
}
