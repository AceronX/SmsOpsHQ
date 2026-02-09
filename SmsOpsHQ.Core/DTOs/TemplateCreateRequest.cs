namespace SmsOpsHQ.Core.DTOs;

// Request body for POST /api/templates.
public sealed class TemplateCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int StoreId { get; set; }
}
