using RLogistics.Abstractions;
using RLogistics.Patterns.Adapter;
using Microsoft.Extensions.Options;

namespace RLogistics.Integrations.Notifications;

/// <summary>
/// Three-mode delivery:
/// Mock — outbox only;
/// PersonalMicrosoft / EnterpriseGraph — mock audit + real Graph send.
/// </summary>
public sealed class CompositeEmailTransport(
    MockOutboxEmailTransport mockOutbox,
    GraphMailTransport graphMail,
    IOptions<NotificationOptions> options,
    ILogger<CompositeEmailTransport> log) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var opts = options.Value;
        var mode = opts.ResolvedMode;

        if (opts.AlwaysAuditToOutbox || mode == NotificationChannelMode.Mock)
            await mockOutbox.SendAsync(message, ct);

        if (mode == NotificationChannelMode.Mock)
            return;

        try
        {
            await graphMail.SendAsync(message, ct);
            log.LogInformation("Graph mail sent to {To} via {Mode}", message.ToAddress, mode);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "Graph mail failed ({Mode}) to {To}. Audit copy remains in Email Outbox.",
                mode, message.ToAddress);
        }
    }
}
