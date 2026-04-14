namespace SmsOpsHQ.Core.DTOs;

// Template data returned in API responses.
public sealed class TemplateDto
{
    public int TemplateId { get; set; }
    public int StoreId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Hotkey { get; set; }
    public string Category { get; set; } = "General";
}
