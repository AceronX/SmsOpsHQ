namespace SmsOpsHQ.Core.DTOs;

// Request body for POST /api/thread/{threadId}/notes.
public sealed class CreateNoteRequest
{
    public int StoreId { get; set; }

    // Note text content
    public string Content { get; set; } = string.Empty;
}
