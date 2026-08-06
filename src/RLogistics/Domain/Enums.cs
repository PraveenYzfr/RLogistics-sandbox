namespace RLogistics.Domain;

public enum UserRole
{
    User = 0,
    Coordinator = 1,
    Admin = 2
}

public enum DispositionType
{
    Sanitize = 0,
    Destroy = 1
}

/// <summary>RLogistics disposal request types.</summary>
public enum RequestType
{
    UsSurplus = 0,
    PointToPoint = 1,
    International = 2,
    RequestABox = 3
}

/// <summary>
/// Coordinator workflow statuses.
/// Rare: PoApproval, OnHold. Cancelled kept for extreme cases.
/// </summary>
public enum RequestStatus
{
    Created = 0,
    Assigned = 1,
    PickupScheduled = 2,
    PickedUp = 3,
    Delivered = 4,
    PoApproval = 5,
    OnHold = 6,
    Cancelled = 7
}

public static class PickupSlots
{
    public static readonly string[] All =
    [
        "08:00–12:00 (Morning)",
        "12:00–16:00 (Afternoon)",
        "16:00–20:00 (Evening)"
    ];
}

public static class RequestTypeLabels
{
    public static string Display(RequestType t) => t switch
    {
        RequestType.UsSurplus => "US Surplus",
        RequestType.PointToPoint => "Point to Point",
        RequestType.International => "International",
        RequestType.RequestABox => "Request a Box",
        _ => t.ToString()
    };
}
