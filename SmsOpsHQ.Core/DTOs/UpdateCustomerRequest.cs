namespace SmsOpsHQ.Core.DTOs;

// Request body for POST /api/customer/{customerId}/update.
public sealed class UpdateCustomerRequest
{
    public string? Notes { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? TagsJson { get; set; }
}
