namespace RLogistics.Domain;

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public ICollection<DisposalRequest> OwnedRequests { get; set; } = new List<DisposalRequest>();
    public ICollection<DisposalRequest> AssignedRequests { get; set; } = new List<DisposalRequest>();
}
