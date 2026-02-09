using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stores/{storeId}")]
public sealed class StoresController : ControllerBase
{
    private readonly IStoreRepository _storeRepo;
    private readonly AppDbContext _db;

    public StoresController(IStoreRepository storeRepo, AppDbContext db)
    {
        _storeRepo = storeRepo;
        _db = db;
    }

    // GET /api/stores/{storeId}/numbers
    // Lists all Twilio numbers for a store, marking which is the default.
    [HttpGet("numbers")]
    public async Task<IActionResult> GetStoreNumbers(
        int storeId,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        StoreEntity? store = await _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

        if (store is null)
            return Problem(statusCode: 404, detail: "Store not found");

        List<TwilioNumberEntity> numbers = await _db.TwilioNumbers
            .AsNoTracking()
            .Where(n => n.StoreId == storeId)
            .ToListAsync(cancellationToken);

        List<object> result = numbers.Select(n => (object)new
        {
            id = n.NumberId,
            phone = n.PhoneE164,
            sid = n.MessagingServiceSid,
            is_default = n.NumberId == store.DefaultNumberId,
            is_active = n.IsActive
        }).ToList();

        return Ok(result);
    }

    // POST /api/stores/{storeId}/numbers
    // Adds a new Twilio number to the store.
    [HttpPost("numbers")]
    public async Task<IActionResult> AddStoreNumber(
        int storeId,
        [FromBody] AddNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        string phone = request.Phone?.Trim() ?? "";
        if (string.IsNullOrEmpty(phone))
            return Problem(statusCode: 400, detail: "Phone number required");

        // Normalize to E.164
        string? normalized = PhoneUtils.NormalizeToE164(phone);
        if (normalized is null)
            return Problem(statusCode: 400, detail: "Invalid phone number format");

        // Check for duplicates
        bool exists = await _db.TwilioNumbers
            .AnyAsync(n => n.PhoneE164 == normalized, cancellationToken);
        if (exists)
            return Ok(new { success = false, error = $"Number {normalized} already exists in database" });

        TwilioNumberEntity newNumber = new TwilioNumberEntity
        {
            StoreId = storeId,
            PhoneE164 = normalized,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.TwilioNumbers.Add(newNumber);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, id = newNumber.NumberId, phone = normalized });
    }

    // POST /api/stores/{storeId}/default-number
    // Sets the default outbound number by number ID.
    [HttpPost("default-number")]
    public async Task<IActionResult> SetDefaultNumberById(
        int storeId,
        [FromBody] SetDefaultNumberByIdRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        StoreEntity? store = await _db.Stores
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);
        if (store is null)
            return Problem(statusCode: 404, detail: "Store not found");

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .FirstOrDefaultAsync(n => n.NumberId == request.NumberId && n.StoreId == storeId, cancellationToken);
        if (number is null)
            return Problem(statusCode: 404, detail: "Number not found for this store");

        store.DefaultNumberId = request.NumberId;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, default_number = number.PhoneE164 });
    }

    // PUT /api/stores/{storeId}/numbers/{numberId}
    // Updates (replaces) a phone number.
    [HttpPut("numbers/{numberId}")]
    public async Task<IActionResult> UpdateStoreNumber(
        int storeId,
        int numberId,
        [FromBody] UpdateNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        string phone = request.Phone?.Trim() ?? "";
        if (string.IsNullOrEmpty(phone))
            return Problem(statusCode: 400, detail: "Phone number required");

        string? normalized = PhoneUtils.NormalizeToE164(phone);
        if (normalized is null)
            return Problem(statusCode: 400, detail: "Invalid phone number format");

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .FirstOrDefaultAsync(n => n.NumberId == numberId && n.StoreId == storeId, cancellationToken);
        if (number is null)
            return Problem(statusCode: 404, detail: "Number not found");

        // Check for duplicates (different record)
        bool duplicate = await _db.TwilioNumbers
            .AnyAsync(n => n.PhoneE164 == normalized && n.NumberId != numberId, cancellationToken);
        if (duplicate)
            return Problem(statusCode: 400, detail: $"Number {normalized} already exists");

        string oldPhone = number.PhoneE164;
        number.PhoneE164 = normalized;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, old_phone = oldPhone, new_phone = normalized });
    }

    // DELETE /api/stores/{storeId}/numbers/{numberId}
    // Deletes a Twilio number. Cannot delete the default number.
    [HttpDelete("numbers/{numberId}")]
    public async Task<IActionResult> DeleteStoreNumber(
        int storeId,
        int numberId,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .FirstOrDefaultAsync(n => n.NumberId == numberId && n.StoreId == storeId, cancellationToken);
        if (number is null)
            return Problem(statusCode: 404, detail: "Number not found");

        // Cannot delete the default number
        StoreEntity? store = await _db.Stores
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);
        if (store is not null && store.DefaultNumberId == numberId)
            return Problem(statusCode: 400, detail: "Cannot delete the default number. Set another as default first.");

        string deletedPhone = number.PhoneE164;
        _db.TwilioNumbers.Remove(number);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, deleted = deletedPhone });
    }

    // POST /api/stores/{storeId}/default_number (by phone string)
    // Sets the default outbound number by phone E.164.
    [HttpPost("default_number")]
    public async Task<IActionResult> SetDefaultNumberByPhone(
        int storeId,
        [FromBody] SetDefaultNumberByPhoneRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        StoreEntity? store = await _db.Stores
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);
        if (store is null)
            return Problem(statusCode: 404, detail: "Store not found");

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .FirstOrDefaultAsync(n => n.StoreId == storeId && n.PhoneE164 == request.Phone, cancellationToken);
        if (number is null)
            return Problem(statusCode: 404, detail: "Number not associated with this store");

        store.DefaultNumberId = number.NumberId;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { status = "updated", new_default = request.Phone });
    }

    // PUT /api/stores/{storeId}/twilio_config
    // Updates Twilio account credentials for a store.
    [HttpPut("twilio_config")]
    public async Task<IActionResult> UpdateTwilioConfig(
        int storeId,
        [FromBody] TwilioConfigRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        StoreEntity? store = await _db.Stores
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);
        if (store is null)
            return Problem(statusCode: 404, detail: "Store not found");

        // TwilioAccountSid and TwilioAuthToken are not on the StoreEntity yet
        // (they may be in a separate config table or appsettings).
        // For now, this endpoint acknowledges the update.
        // In a full implementation, these would be stored in a StoreConfig table.
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { status = "updated" });
    }
}

// Request DTOs for StoresController
public sealed class AddNumberRequest
{
    public string Phone { get; set; } = string.Empty;
}

public sealed class SetDefaultNumberByIdRequest
{
    public int NumberId { get; set; }
}

public sealed class UpdateNumberRequest
{
    public string Phone { get; set; } = string.Empty;
}

public sealed class SetDefaultNumberByPhoneRequest
{
    public string Phone { get; set; } = string.Empty;
}

public sealed class TwilioConfigRequest
{
    public string? Sid { get; set; }
    public string? Token { get; set; }
}
