namespace SmsOpsHQ.Core.Services;

/// <summary>Resolves and validates the exact Twilio number used for an outbound message.</summary>
public interface IOutboundNumberResolver
{
    Task<OutboundNumberResolution> ResolveAsync(
        int storeId,
        int? twilioNumberId,
        CancellationToken cancellationToken = default);
}

public sealed record OutboundNumberResolution(int TwilioNumberId, string PhoneE164);

public sealed class OutboundNumberValidationException : Exception
{
    public OutboundNumberValidationException(string message) : base(message) { }
}
