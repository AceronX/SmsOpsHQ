namespace SmsOpsHQ.Core.DTOs;

// Request body for POST /api/reviews/send.
public sealed class SendReviewRequest
{
    public int StoreId { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
}

// Review request data returned in API responses.
public sealed class ReviewRequestDto
{
    public int ReviewRequestId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

// Review channel data returned in API responses.
public sealed class ReviewChannelDto
{
    public int ReviewChannelId { get; set; }
    public int StoreId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

// Request body for POST /api/reviews/channels.
public sealed class CreateReviewChannelRequest
{
    public int StoreId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

// Request body for PUT /api/reviews/channels/{id}.
public sealed class UpdateReviewChannelRequest
{
    public string PlatformName { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
