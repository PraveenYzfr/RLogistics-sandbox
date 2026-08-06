namespace RLogistics.Domain;

/// <summary>Mock / audit trail for Teams-style notifications (and delivery log for real providers).</summary>
public class TeamsOutbox
{
    public int Id { get; set; }
    public string Channel { get; set; } = "Mock"; // Mock | Webhook | Graph
    public string? ToHint { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? RequestId { get; set; }
    public string? ProviderResult { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
