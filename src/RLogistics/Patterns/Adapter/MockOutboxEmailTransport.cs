using RLogistics.Abstractions;
using RLogistics.Data;
using RLogistics.Domain;

namespace RLogistics.Patterns.Adapter;

/// <summary>
/// Adapter: converts domain EmailMessage → EmailOutbox persistence (mock SMTP).
/// Real Graph is layered by CompositeEmailTransport + GraphMailTransport.
/// </summary>
public sealed class MockOutboxEmailTransport(RLogisticsDbContext db) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        db.EmailOutbox.Add(new EmailOutbox
        {
            ToAddress = message.ToAddress,
            Subject = message.Subject,
            Body = message.Body,
            RequestId = message.RequestId,
            TemplateCode = message.TemplateCode,
            StatusFrom = message.StatusFrom,
            StatusTo = message.StatusTo,
            CreatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
