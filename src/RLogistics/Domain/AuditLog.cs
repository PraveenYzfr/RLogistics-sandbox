namespace RLogistics.Domain;

public class AuditLog
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public DisposalRequest Request { get; set; } = null!;
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.UtcNow;
}
