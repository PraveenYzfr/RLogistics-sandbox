using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;
using RLogistics.Integrations.Notifications;
using RLogistics.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RLogistics.Controllers;

/// <summary>
/// Mail/Teams mode status, Graph device-code login, test send, Teams outbox.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(
    IOptions<NotificationOptions> options,
    PersonalGraphTokenStore tokens,
    IEmailTransport email,
    ITeamsNotifier teams,
    RLogisticsDbContext db) : ControllerBase
{
    [HttpGet("status")]
    [Authorize(Policy = RLogisticsPermissions.EmailOutboxRead)]
    public ActionResult<object> Status()
    {
        var o = options.Value;
        return Ok(new
        {
            mode = o.Mode,
            resolvedMode = o.ResolvedMode.ToString(),
            alwaysAuditToOutbox = o.AlwaysAuditToOutbox,
            notifyTeamsOnEmail = o.NotifyTeamsOnEmail,
            graph = new
            {
                clientIdConfigured = !string.IsNullOrWhiteSpace(o.Graph.ClientId),
                tenantId = o.Graph.TenantId,
                senderUserId = o.Graph.SenderUserId,
                signedInUpn = tokens.LastSignedInUpn,
                lastDeviceCode = tokens.LastDeviceCode,
                lastVerificationUrl = tokens.LastVerificationUrl,
                lastDeviceCodeMessage = tokens.LastDeviceCodeMessage,
                loginInProgress = tokens.LoginInProgress
            },
            teams = new
            {
                provider = o.Teams.Provider,
                webhookConfigured = !string.IsNullOrWhiteSpace(o.Teams.IncomingWebhookUrl),
                chatId = o.Teams.ChatId,
                teamId = o.Teams.TeamId,
                channelId = o.Teams.ChannelId
            }
        });
    }

    /// <summary>
    /// Start device-code login for PersonalMicrosoft (non-blocking poll via /status).
    /// </summary>
    [HttpPost("graph/login")]
    [Authorize(Policy = RLogisticsPermissions.AdminConfig)]
    public ActionResult<object> StartGraphLogin()
    {
        if (string.IsNullOrWhiteSpace(options.Value.Graph.ClientId))
            return BadRequest(new { error = "Set Notifications:Graph:ClientId first (Entra app registration)." });

        tokens.StartInteractiveLogin();
        return Accepted(new
        {
            message = "Device-code flow started. Open verification URL and enter the code (see /api/notifications/status).",
            deviceCode = tokens.LastDeviceCode,
            verificationUrl = tokens.LastVerificationUrl,
            detail = tokens.LastDeviceCodeMessage
        });
    }

    [HttpPost("test-email")]
    [Authorize(Policy = RLogisticsPermissions.AdminConfig)]
    public async Task<ActionResult<object>> TestEmail([FromBody] TestEmailDto? dto, CancellationToken ct)
    {
        var to = string.IsNullOrWhiteSpace(dto?.To)
            ? "you@example.com"
            : dto!.To.Trim();
        await email.SendAsync(new EmailMessage(
            to,
            "[RLogistics] Test notification",
            "Hello from RLogistics Core notification stack.\n\nMode: " + options.Value.Mode +
            "\nIf PersonalMicrosoft/EnterpriseGraph is on and ClientId is set, this also hits Microsoft Graph.",
            null,
            "Test",
            null,
            "Test"), ct);
        return Ok(new { ok = true, to, mode = options.Value.Mode });
    }

    [HttpPost("test-teams")]
    [Authorize(Policy = RLogisticsPermissions.AdminConfig)]
    public async Task<ActionResult<object>> TestTeams(CancellationToken ct)
    {
        await teams.NotifyAsync(new TeamsMessage(
            "RLogistics test message",
            "Hello from RLogistics Core Teams stack at " + DateTime.UtcNow.ToString("u") +
            "\nProvider: " + options.Value.Teams.Provider,
            null,
            null), ct);
        return Ok(new { ok = true, provider = options.Value.Teams.Provider });
    }

    [HttpGet("teams-outbox")]
    [Authorize(Policy = RLogisticsPermissions.EmailOutboxRead)]
    public async Task<ActionResult<object>> TeamsOutbox(CancellationToken ct)
    {
        var rows = await db.TeamsOutbox.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .Select(t => new
            {
                t.Id, t.Channel, t.ToHint, t.Title, t.Body, t.RequestId,
                t.ProviderResult, t.CreatedAt, t.SentAt
            })
            .ToListAsync(ct);
        return Ok(rows);
    }
}

public sealed record TestEmailDto(string? To);
