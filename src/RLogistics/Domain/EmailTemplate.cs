namespace RLogistics.Domain;

public class EmailTemplate
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SubjectTemplate { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
