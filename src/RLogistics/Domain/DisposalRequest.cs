namespace RLogistics.Domain;

public class DisposalRequest
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public int RequestorUserId { get; set; }
    public AppUser Requestor { get; set; } = null!;

    // Contact / user details (on the request)
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactDepartment { get; set; }

    // Facility
    public string Site { get; set; } = string.Empty; // facility display name
    public string? FacilityCode { get; set; }
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public string? Room { get; set; }

    // Pickup address
    public string PickupAddressLine1 { get; set; } = string.Empty;
    public string? PickupAddressLine2 { get; set; }
    public string PickupCity { get; set; } = string.Empty;
    public string? PickupState { get; set; }
    public string? PickupPostalCode { get; set; }
    public string PickupCountry { get; set; } = "USA";
    public DateTime? PreferredPickupDate { get; set; }
    public string? PickupInstructions { get; set; }

    public DispositionType DispositionType { get; set; }
    public RequestType RequestType { get; set; } = RequestType.UsSurplus;
    public RequestStatus Status { get; set; } = RequestStatus.Created;
    public int? AssignedCoordinatorUserId { get; set; }
    public AppUser? AssignedCoordinator { get; set; }
    public string? Notes { get; set; }
    public string? CoordinatorNotes { get; set; }

    public int? TransportVendorId { get; set; }
    public Vendor? TransportVendor { get; set; }
    public int? ProcessingVendorId { get; set; }
    public Vendor? ProcessingVendor { get; set; }
    public DateTime? ScheduledPickupDate { get; set; }
    public string? ScheduledPickupSlot { get; set; }

    /// <summary>When devices should have been turned in for disposal / return to RLogistics.</summary>
    public DateTime? ExpectedDeviceReturnDate { get; set; }
    public DateTime? LastReturnReminderAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<AssetLine> Assets { get; set; } = new List<AssetLine>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
}
