using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

/// <summary>
/// Applies a Twilio delivery-status callback to the local outbound message
/// and emits the realtime push so the desktop UI updates without polling.
/// Same logic for HTTP webhook and SignalR relay from the Hub.
/// </summary>
public sealed class MessageStatusProcessor : IMessageStatusProcessor
{
    private readonly IMessageRepository _messageRepo;
    private readonly IRealtimeService _realtimeService;
    private readonly ILogger<MessageStatusProcessor> _logger;

    public MessageStatusProcessor(
        IMessageRepository messageRepo,
        IRealtimeService realtimeService,
        ILogger<MessageStatusProcessor> logger)
    {
        _messageRepo = messageRepo;
        _realtimeService = realtimeService;
        _logger = logger;
    }

    public async Task<MessageStatusProcessingResult> ProcessAsync(MessageStatusUpdate update, CancellationToken cancellationToken = default)
    {
        if (update is null)
            return new MessageStatusProcessingResult(MessageStatusResultKind.Error, null, null, null, "Update was null.");

        string messageSid = update.MessageSid ?? string.Empty;
        string messageStatus = update.MessageStatus ?? string.Empty;
        string? errorCode = update.ErrorCode;

        if (string.IsNullOrEmpty(messageSid))
            return new MessageStatusProcessingResult(MessageStatusResultKind.Empty, null, null, null, "missing_sid");

        if (string.IsNullOrEmpty(messageStatus))
            return new MessageStatusProcessingResult(MessageStatusResultKind.Empty, null, null, null, "missing_status");

        // Capitalize to match the legacy controller convention (Twilio sends
        // lowercase "delivered", "failed", etc; existing DB rows use Title Case).
        string normalizedStatus = char.ToUpper(messageStatus[0]) + messageStatus[1..];

        await _messageRepo.UpdateStatusBySidAsync(
            messageSid, normalizedStatus, errorCode, errorText: null, cancellationToken);

        Message? message = await _messageRepo.FindBySidAsync(messageSid, cancellationToken);
        if (message is null)
        {
            _logger.LogWarning(
                "Status callback for unknown SID; nothing to update. sid={Sid} status={Status}",
                messageSid, normalizedStatus);
            return new MessageStatusProcessingResult(MessageStatusResultKind.NotFound, null, null, null, "sid_not_in_db");
        }

        await _realtimeService.PushMessageStatusAsync(
            message.StoreId,
            message.ThreadId,
            message.MessageId,
            messageSid,
            normalizedStatus,
            errorCode,
            cancellationToken);

        _logger.LogInformation(
            "Status callback applied: store={StoreId} message={MessageId} sid={Sid} status={Status}",
            message.StoreId, message.MessageId, messageSid, normalizedStatus);

        return new MessageStatusProcessingResult(
            MessageStatusResultKind.Updated,
            message.StoreId,
            message.ThreadId,
            message.MessageId);
    }
}
