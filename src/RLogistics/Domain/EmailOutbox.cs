namespace RLogistics.Domain;

public class EmailOutbox
{
    public int Id { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? RequestId { get; set; }
    public string? TemplateCode { get; set; }
    public string? StatusFrom { get; set; }
    public string? StatusTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
