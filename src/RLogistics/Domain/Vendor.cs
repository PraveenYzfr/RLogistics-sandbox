namespace RLogistics.Domain;

public enum VendorType
{
    Transport = 0,
    Processing = 1
}

public class Vendor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public VendorType Type { get; set; }
    public string? ServiceArea { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}
