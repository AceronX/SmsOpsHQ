using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

/// <summary>
/// Raised after an outbound review attempt has been persisted as Mock or Failed.
/// Carries the recorded attempt so the API can return structured ProblemDetails.
/// </summary>
public sealed class OutboundSendException : Exception
{
    public OutboundSendException(string message, ReviewRequestDto attempt) : base(message)
    {
        Attempt = attempt;
    }

    public ReviewRequestDto Attempt { get; }
}
