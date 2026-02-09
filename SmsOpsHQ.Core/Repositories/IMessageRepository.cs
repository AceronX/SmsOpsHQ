using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Message entities.
public interface IMessageRepository
{
    // Find a message by its Twilio SID (idempotency check).
    Task<Message?> FindBySidAsync(string twilioSid, CancellationToken cancellationToken = default);

    // Create an outbound message. Sets Direction="Outbound", Status="Queued".
    Task<Message> CreateOutboundAsync(
        int storeId, int threadId, string storePhone,
        string fromE164, string toE164, string body,
        string? mediaJson, string category, int? sentByUserId,
        CancellationToken cancellationToken = default);

    // Create an inbound message. Sets Direction="Inbound", Status="Received".
    Task<Message> CreateInboundAsync(
        int storeId, int threadId, string storePhone,
        string fromE164, string toE164, string body,
        string? mediaJson, string category,
        CancellationToken cancellationToken = default);

    // Update a sent message with Twilio SID and delivery status.
    Task UpdateSentAsync(int messageId, string twilioSid, string status,
        CancellationToken cancellationToken = default);

    // Update delivery status by Twilio SID (webhook callback).
    Task UpdateStatusBySidAsync(string twilioSid, string status,
        string? errorCode, string? errorText,
        CancellationToken cancellationToken = default);

    // Get messages for a thread, ordered by CreatedAt DESC.
    Task<List<Message>> GetByThreadAsync(int storeId, int threadId, int limit = 50,
        CancellationToken cancellationToken = default);

    // Get the most recent message in a thread.
    Task<Message?> GetLastMessageAsync(int threadId,
        CancellationToken cancellationToken = default);

    // Create an internal note. Sets Direction="Note", Status="Internal".
    Task<Message> CreateNoteAsync(int storeId, int threadId, string content, int userId,
        CancellationToken cancellationToken = default);

    // Get a paged list of messages for a store with optional category and thread filters.
    // Returns the matching messages and the total count before pagination.
    Task<(List<Message> Messages, int TotalCount)> GetPagedAsync(
        int storeId, string? category, int? threadId, int limit, int offset,
        CancellationToken cancellationToken = default);

    // Get message counts grouped by category for a store. Optionally scoped to a thread.
    Task<Dictionary<string, int>> GetCountsByCategoryAsync(
        int storeId, int? threadId,
        CancellationToken cancellationToken = default);

    // Get all messages for a store, ordered by ThreadId then CreatedAt.
    Task<List<Message>> GetAllByStoreAsync(int storeId,
        CancellationToken cancellationToken = default);
}
