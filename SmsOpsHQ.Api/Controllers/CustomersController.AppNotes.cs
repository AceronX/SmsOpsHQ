using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Api.Controllers;

public sealed partial class CustomersController
{
    [HttpGet("customers/{customerKey:int}/app-notes")]
    public async Task<IActionResult> GetCustomerAppNotes(
        int customerKey,
        CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .Where(candidate => candidate.CustomerKey == customerKey)
            .Select(candidate => new { candidate.StoreId, candidate.CustomerKey })
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
            return Problem(statusCode: 404, detail: $"Customer Key {customerKey} not found");

        if (!User.CanAccessStore(customer.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this customer");

        IReadOnlyList<CustomerAppNote> notes = await _customerAppNoteRepo.GetByCustomerAsync(
            customer.StoreId,
            customerKey,
            cancellationToken);

        return Ok(notes.Select(MapAppNote));
    }

    [HttpPost("customers/{customerKey:int}/app-notes")]
    public async Task<IActionResult> CreateCustomerAppNote(
        int customerKey,
        [FromBody] CreateCustomerAppNoteRequest request,
        CancellationToken cancellationToken)
    {
        string content = (request.Content ?? string.Empty).Trim();
        if (content.Length == 0)
            return Problem(statusCode: 400, detail: "Content is required");
        if (content.Length > 4000)
            return Problem(statusCode: 400, detail: "Content must be 4000 characters or fewer");

        var customer = await _db.Customers
            .AsNoTracking()
            .Where(candidate => candidate.CustomerKey == customerKey)
            .Select(candidate => new { candidate.StoreId, candidate.CustomerKey })
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
            return Problem(statusCode: 404, detail: $"Customer Key {customerKey} not found");

        if (!User.CanAccessStore(customer.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this customer");

        int userId = User.GetUserId();
        if (userId <= 0)
            return Problem(statusCode: 403, detail: "Authenticated user is unavailable");

        CustomerAppNote note = await _customerAppNoteRepo.CreateAsync(
            customer.StoreId,
            customerKey,
            content,
            userId,
            cancellationToken);

        return Ok(MapAppNote(note));
    }

    private static CustomerAppNoteDto MapAppNote(CustomerAppNote note) => new()
    {
        CustomerAppNoteId = note.CustomerAppNoteId,
        StoreId = note.StoreId,
        CustomerKey = note.CustomerKey,
        Content = note.Content,
        CreatedByUserId = note.CreatedByUserId,
        CreatedByUsername = note.CreatedByUsername,
        CreatedAtUtc = note.CreatedAtUtc,
        UpdatedAtUtc = note.UpdatedAtUtc
    };
}
