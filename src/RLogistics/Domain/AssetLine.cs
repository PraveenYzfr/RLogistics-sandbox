namespace RLogistics.Domain;

public class AssetLine
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public DisposalRequest Request { get; set; } = null!;
    public string AssetType { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceGuid { get; set; }
    public string? AssetTag { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Condition { get; set; }
}
