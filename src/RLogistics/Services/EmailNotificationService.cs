using System.Text;
using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;
using Microsoft.EntityFrameworkCore;

namespace RLogistics.Services;

/// <summary>
/// Mock SMTP orchestration: templates + transport adapter.
/// </summary>
public class EmailNotificationService(
    RLogisticsDbContext db,
    IEmailTransport transport,
    ITeamsNotifier teams,
    Microsoft.Extensions.Options.IOptions<Integrations.Notifications.NotificationOptions> notifyOptions)
    : IEmailNotificationService
{
    public async Task SendStatusChangeAsync(DisposalRequest request, RequestStatus from, RequestStatus to, CancellationToken ct = default)
    {
        // Prefer status-specific template, then dedicated legacy codes, then generic StatusChanged.
        var codes = new List<string> { $"Status_{to}" };
        if (to == RequestStatus.PickupScheduled)
            codes.Add("PickupScheduled");
        codes.Add("StatusChanged");

        var template = await ResolveTemplateAsync(codes, ct);
        if (template is null) return;

        await EnsureNavAsync(request, ct);

        var toAddresses = RecipientList(
            Prefer(request.ContactEmail, request.Requestor?.Email),
            request.AssignedCoordinator?.Email);

        foreach (var addr in toAddresses)
        {
            await WriteOutboxAsync(template, request, addr, from.ToString(), to.ToString(), extra: null, ct);
        }

        await MaybeTeamsAsync(
            $"RLogistics {request.RequestNumber}: {from} → {to}",
            $"Site: {request.Site}\nContact: {request.ContactName}\nNotes: {request.Notes ?? "(none)"}",
            request.Id, ct);
    }

    public async Task SendClarificationAsync(DisposalRequest request, string? question = null, CancellationToken ct = default)
    {
        var template = await ResolveTemplateAsync(["ClarificationSent", "StatusChanged"], ct);
        if (template is null) return;

        await EnsureNavAsync(request, ct);
        var extra = new Dictionary<string, string>
        {
            ["{{ClarificationQuestion}}"] = question ?? "(see RLogistics)"
        };
        var to = Prefer(request.ContactEmail, request.Requestor?.Email);
        if (to is null) return;
        await WriteOutboxAsync(template, request, to, request.Status.ToString(), RequestStatus.OnHold.ToString(), extra, ct);
        await MaybeTeamsAsync(
            $"RLogistics {request.RequestNumber}: clarification",
            question ?? "(see RLogistics)",
            request.Id, ct);
    }

    /// <summary>Send quote-request emails to selected transport and/or processing vendors.</summary>
    public async Task<(int Sent, string Detail)> SendVendorQuotesAsync(DisposalRequest request, CancellationToken ct = default)
    {
        await EnsureNavAsync(request, ct);
        await db.Entry(request).Reference(r => r.TransportVendor).LoadAsync(ct);
        await db.Entry(request).Reference(r => r.ProcessingVendor).LoadAsync(ct);

        var sent = 0;
        var notes = new List<string>();

        if (request.TransportVendor is { IsActive: true } tv)
        {
            var ok = await SendVendorQuoteAsync(request, tv, VendorType.Transport, ct);
            if (ok) { sent++; notes.Add($"transport→{tv.Name}"); }
            else notes.Add($"transport skipped (no email): {tv.Name}");
        }
        else
            notes.Add("no transport vendor selected");

        if (request.ProcessingVendor is { IsActive: true } pv)
        {
            var ok = await SendVendorQuoteAsync(request, pv, VendorType.Processing, ct);
            if (ok) { sent++; notes.Add($"processing→{pv.Name}"); }
            else notes.Add($"processing skipped (no email): {pv.Name}");
        }
        else
            notes.Add("no processing vendor selected");

        if (sent > 0)
        {
            await MaybeTeamsAsync(
                $"RLogistics {request.RequestNumber}: vendor quotes",
                string.Join("; ", notes),
                request.Id, ct);
        }

        return (sent, string.Join("; ", notes));
    }

    public async Task<bool> SendVendorQuoteAsync(DisposalRequest request, Vendor vendor, VendorType type, CancellationToken ct = default)
    {
        var email = string.IsNullOrWhiteSpace(vendor.Email)
            ? $"quotes+{vendor.Id}@vendor.demo.local"
            : vendor.Email.Trim();

        var codes = type == VendorType.Transport
            ? new[] { "VendorQuote_Transport", "VendorQuote", "StatusChanged" }
            : new[] { "VendorQuote_Processing", "VendorQuote", "StatusChanged" };

        var template = await ResolveTemplateAsync(codes, ct);
        if (template is null) return false;

        await EnsureNavAsync(request, ct);
        var extra = new Dictionary<string, string>
        {
            ["{{VendorName}}"] = vendor.Name,
            ["{{VendorType}}"] = type.ToString(),
            ["{{VendorEmail}}"] = email
        };

        await WriteOutboxAsync(template, request, email, null, "QuoteRequested", extra, ct);
        return true;
    }

    /// <summary>
    /// Reminder to team contact / requestor when devices are past due and not yet picked up.
    /// </summary>
    public async Task SendDeviceReturnReminderAsync(DisposalRequest request, CancellationToken ct = default)
    {
        var template = await ResolveTemplateAsync(["DeviceReturnReminder", "StatusChanged"], ct);
        if (template is null) return;

        await EnsureNavAsync(request, ct);
        var due = request.ExpectedDeviceReturnDate?.ToString("yyyy-MM-dd") ?? "(not set)";
        var extra = new Dictionary<string, string>
        {
            ["{{ExpectedReturnDate}}"] = due,
            ["{{DaysOverdue}}"] = request.ExpectedDeviceReturnDate is DateTime d
                ? Math.Max(0, (int)(DateTime.UtcNow.Date - d.Date).TotalDays).ToString()
                : "0"
        };

        var toAddresses = RecipientList(
            Prefer(request.ContactEmail, request.Requestor?.Email),
            request.AssignedCoordinator?.Email);

        foreach (var addr in toAddresses)
            await WriteOutboxAsync(template, request, addr, request.Status.ToString(), request.Status.ToString(), extra, ct);

        await MaybeTeamsAsync(
            $"RLogistics {request.RequestNumber}: device return reminder",
            $"Due {due}; overdue days: {extra["{{DaysOverdue}}"]}",
            request.Id, ct);

        request.LastReturnReminderAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task MaybeTeamsAsync(string title, string body, int? requestId, CancellationToken ct)
    {
        if (!notifyOptions.Value.NotifyTeamsOnEmail) return;
        await teams.NotifyAsync(new TeamsMessage(title, body, requestId), ct);
    }

    public async Task<int> SendOverdueDeviceReturnRemindersAsync(CancellationToken ct = default)
    {
        var graceHours = await GetConfigIntAsync("DeviceReturnReminderCooldownHours", 24, ct);
        var cutoff = DateTime.UtcNow.AddHours(-graceHours);
        var today = DateTime.UtcNow.Date;

        var overdue = await db.Requests
            .Include(r => r.Requestor)
            .Include(r => r.AssignedCoordinator)
            .Include(r => r.Assets)
            .Where(r =>
                r.ExpectedDeviceReturnDate != null &&
                r.ExpectedDeviceReturnDate < today &&
                r.Status != RequestStatus.PickedUp &&
                r.Status != RequestStatus.Delivered &&
                r.Status != RequestStatus.Cancelled &&
                (r.LastReturnReminderAt == null || r.LastReturnReminderAt < cutoff))
            .ToListAsync(ct);

        foreach (var r in overdue)
            await SendDeviceReturnReminderAsync(r, ct);

        return overdue.Count;
    }

    private async Task WriteOutboxAsync(
        EmailTemplate template,
        DisposalRequest request,
        string toAddress,
        string? statusFrom,
        string? statusTo,
        Dictionary<string, string>? extra,
        CancellationToken ct)
    {
        string Replace(string input)
        {
            var s = ApplyTokens(input, request, statusFrom, statusTo);
            if (extra is not null)
            {
                foreach (var kv in extra)
                    s = s.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);
            }
            return s;
        }

        await transport.SendAsync(new EmailMessage(
            toAddress,
            Replace(template.SubjectTemplate),
            Replace(template.BodyTemplate),
            request.Id,
            template.Code,
            statusFrom,
            statusTo), ct);
    }

    private static string ApplyTokens(string input, DisposalRequest request, string? statusFrom, string? statusTo)
    {
        var assets = request.Assets ?? [];
        var assetLines = new StringBuilder();
        foreach (var a in assets)
        {
            assetLines.AppendLine(
                $"- {a.Quantity}x {a.AssetType} | {a.Manufacturer} {a.Model} | GUID {a.DeviceGuid ?? "n/a"} | SN {a.SerialNumber ?? "n/a"}");
        }

        var pickup = string.Join(", ", new[]
        {
            request.PickupAddressLine1,
            request.PickupAddressLine2,
            request.PickupCity,
            request.PickupState,
            request.PickupPostalCode,
            request.PickupCountry
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return input
            .Replace("{{RequestNumber}}", request.RequestNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Site}}", request.Site, StringComparison.OrdinalIgnoreCase)
            .Replace("{{StatusFrom}}", statusFrom ?? request.Status.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{StatusTo}}", statusTo ?? request.Status.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{RequestorName}}", request.Requestor?.DisplayName ?? request.ContactName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{ContactName}}", request.ContactName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{ContactEmail}}", request.ContactEmail, StringComparison.OrdinalIgnoreCase)
            .Replace("{{ContactPhone}}", request.ContactPhone ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{Notes}}", request.Notes ?? "(none)", StringComparison.OrdinalIgnoreCase)
            .Replace("{{CoordinatorNotes}}", request.CoordinatorNotes ?? "(none)", StringComparison.OrdinalIgnoreCase)
            .Replace("{{Disposition}}", request.DispositionType.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{RequestType}}", RequestTypeLabels.Display(request.RequestType), StringComparison.OrdinalIgnoreCase)
            .Replace("{{PickupAddress}}", pickup, StringComparison.OrdinalIgnoreCase)
            .Replace("{{PreferredPickupDate}}", request.PreferredPickupDate?.ToString("yyyy-MM-dd") ?? "TBD", StringComparison.OrdinalIgnoreCase)
            .Replace("{{ScheduledPickupDate}}", request.ScheduledPickupDate?.ToString("yyyy-MM-dd") ?? "TBD", StringComparison.OrdinalIgnoreCase)
            .Replace("{{ScheduledPickupSlot}}", request.ScheduledPickupSlot ?? "TBD", StringComparison.OrdinalIgnoreCase)
            .Replace("{{ExpectedReturnDate}}", request.ExpectedDeviceReturnDate?.ToString("yyyy-MM-dd") ?? "TBD", StringComparison.OrdinalIgnoreCase)
            .Replace("{{AssetList}}", assetLines.Length == 0 ? "(no assets listed)" : assetLines.ToString().TrimEnd(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{AssetCount}}", assets.Sum(a => a.Quantity).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{TransportVendor}}", request.TransportVendor?.Name ?? "(not selected)", StringComparison.OrdinalIgnoreCase)
            .Replace("{{ProcessingVendor}}", request.ProcessingVendor?.Name ?? "(not selected)", StringComparison.OrdinalIgnoreCase)
            .Replace("{{CoordinatorEmail}}", request.AssignedCoordinator?.Email ?? "(unassigned)", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureNavAsync(DisposalRequest request, CancellationToken ct)
    {
        if (request.Requestor is null)
            await db.Entry(request).Reference(r => r.Requestor).LoadAsync(ct);
        if (request.AssignedCoordinator is null && request.AssignedCoordinatorUserId is not null)
            await db.Entry(request).Reference(r => r.AssignedCoordinator).LoadAsync(ct);
        if (request.Assets is null || request.Assets.Count == 0)
            await db.Entry(request).Collection(r => r.Assets).LoadAsync(ct);
    }

    private async Task<EmailTemplate?> ResolveTemplateAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        foreach (var code in codes)
        {
            var t = await db.EmailTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == code && x.IsActive, ct);
            if (t is not null) return t;
        }
        return null;
    }

    private async Task<int> GetConfigIntAsync(string key, int defaultValue, CancellationToken ct)
    {
        var row = await db.AppConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
        return row is not null && int.TryParse(row.Value, out var n) ? n : defaultValue;
    }

    private static string? Prefer(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (!string.IsNullOrWhiteSpace(b) ? b.Trim() : null);

    private static List<string> RecipientList(params string?[] addresses)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in addresses)
        {
            if (!string.IsNullOrWhiteSpace(a))
                set.Add(a.Trim());
        }
        return set.ToList();
    }
}
