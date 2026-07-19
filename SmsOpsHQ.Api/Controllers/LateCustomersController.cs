using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/late-customers")]
public sealed class LateCustomersController : ControllerBase
{
    private readonly ILateTicketPullRepository _pullRepository;
    private readonly AppDbContext _db;

    public LateCustomersController(
        ILateTicketPullRepository pullRepository,
        AppDbContext db)
    {
        _pullRepository = pullRepository;
        _db = db;
    }

    [HttpGet("pull-list")]
    public async Task<IActionResult> GetPullList(
        [FromQuery] int storeId,
        CancellationToken cancellationToken)
    {
        if (storeId <= 0)
            return Problem(statusCode: 400, detail: "A valid storeId is required");
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        IReadOnlyList<LateTicketPull> pulls = await _pullRepository.GetByStoreAsync(
            storeId,
            cancellationToken);
        return Ok(pulls.Select(Map));
    }

    [HttpPost("pull-list")]
    public async Task<IActionResult> MoveToPullList(
        [FromBody] PullLateTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StoreId <= 0 || request.TicketKey <= 0 || request.CustomerKey <= 0)
            return Problem(statusCode: 400, detail: "storeId, ticketKey, and customerKey are required");
        if (!User.CanAccessStore(request.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        string? reason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();
        if (reason?.Length > 500)
            return Problem(statusCode: 400, detail: "Reason must be 500 characters or fewer");

        LateTicketPull? existing = await _pullRepository.GetAsync(
            request.StoreId,
            request.TicketKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.CustomerKey != request.CustomerKey)
                return Problem(statusCode: 400, detail: "Ticket does not belong to the requested customer");
            return Ok(Map(existing));
        }

        int? ticketCustomerKey = await _db.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Key == request.TicketKey)
            .Select(ticket => (int?)ticket.CustomerKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (ticketCustomerKey is null)
            return Problem(statusCode: 404, detail: $"Ticket Key {request.TicketKey} not found");
        if (ticketCustomerKey.Value != request.CustomerKey)
            return Problem(statusCode: 400, detail: "Ticket does not belong to the requested customer");

        int? ticketStoreId = await _db.Customers
            .AsNoTracking()
            .Where(customer => customer.CustomerKey == request.CustomerKey)
            .Select(customer => (int?)customer.StoreId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ticketStoreId is null)
            return Problem(statusCode: 404, detail: $"Customer Key {request.CustomerKey} not found");
        if (ticketStoreId.Value != request.StoreId)
            return Problem(statusCode: 400, detail: "Ticket does not belong to the requested store");

        int userId = User.GetUserId();
        if (userId <= 0)
            return Problem(statusCode: 403, detail: "Authenticated user is unavailable");

        LateTicketPull pull = await _pullRepository.PullAsync(
            request.StoreId,
            request.TicketKey,
            request.CustomerKey,
            reason,
            userId,
            cancellationToken);

        return Ok(Map(pull));
    }

    [HttpDelete("pull-list/{ticketKey:int}")]
    public async Task<IActionResult> RestoreFromPullList(
        int ticketKey,
        [FromQuery] int storeId,
        CancellationToken cancellationToken)
    {
        if (storeId <= 0 || ticketKey <= 0)
            return Problem(statusCode: 400, detail: "A valid storeId and ticketKey are required");
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        await _pullRepository.RestoreAsync(storeId, ticketKey, cancellationToken);
        return NoContent();
    }

    private static object Map(LateTicketPull pull) => new
    {
        late_ticket_pull_id = pull.LateTicketPullId,
        store_id = pull.StoreId,
        ticket_key = pull.TicketKey,
        customer_key = pull.CustomerKey,
        reason = pull.Reason,
        pulled_by_user_id = pull.PulledByUserId,
        pulled_at_utc = pull.PulledAtUtc
    };
}

public sealed class PullLateTicketRequest
{
    public int StoreId { get; set; }
    public int TicketKey { get; set; }
    public int CustomerKey { get; set; }
    public string? Reason { get; set; }
}
