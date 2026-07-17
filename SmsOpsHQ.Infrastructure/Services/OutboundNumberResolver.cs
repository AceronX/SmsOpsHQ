using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

public sealed class OutboundNumberResolver : IOutboundNumberResolver
{
    private readonly AppDbContext _db;

    public OutboundNumberResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<OutboundNumberResolution> ResolveAsync(
        int storeId,
        int? twilioNumberId,
        CancellationToken cancellationToken = default)
    {
        TwilioNumberEntity? number;

        if (twilioNumberId.HasValue)
        {
            number = await _db.TwilioNumbers
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.NumberId == twilioNumberId.Value, cancellationToken);

            if (number is null)
                throw new OutboundNumberValidationException("Selected Twilio number was not found.");
            if (number.StoreId != storeId)
                throw new OutboundNumberValidationException("Selected Twilio number does not belong to this store.");
            if (!number.IsActive)
                throw new OutboundNumberValidationException("Selected Twilio number is inactive.");
        }
        else
        {
            StoreEntity? store = await _db.Stores
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

            if (store is null)
                throw new OutboundNumberValidationException("Store not found.");
            if (store.DefaultNumberId <= 0)
                throw new OutboundNumberValidationException("No default Twilio number is configured for this store.");

            number = await _db.TwilioNumbers
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.NumberId == store.DefaultNumberId, cancellationToken);

            if (number is null || number.StoreId != storeId)
                throw new OutboundNumberValidationException("The store default Twilio number is invalid.");
            if (!number.IsActive)
                throw new OutboundNumberValidationException("The store default Twilio number is inactive.");
        }

        string normalized = PhoneUtils.NormalizeToE164(number.PhoneE164)
            ?? throw new OutboundNumberValidationException("The selected Twilio number is not a valid E.164 phone number.");

        return new OutboundNumberResolution(number.NumberId, normalized);
    }
}
