namespace RLogistics.Domain;

public class Clarification
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public DisposalRequest Request { get; set; } = null!;
    public int ActorUserId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
