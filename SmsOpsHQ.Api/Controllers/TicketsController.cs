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
[Route("api")]
public sealed class TicketsController : ControllerBase
{
    private readonly ITicketRepository _ticketRepo;
    private readonly AppDbContext _db;

    public TicketsController(ITicketRepository ticketRepo, AppDbContext db)
    {
        _ticketRepo = ticketRepo;
        _db = db;
    }

    // GET /api/ticket/{ticketKey}/context
    // Resolves a ticket to its customer. Returns the local CustomerId
    // for navigating to the customer context panel in the desktop client.
    [HttpGet("ticket/{ticketKey}/context")]
    public async Task<IActionResult> GetTicketContext(
        int ticketKey,
        CancellationToken cancellationToken)
    {
        int? userStoreId = User.GetStoreId();
        if (!User.IsHqUser() && userStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        // Load ticket from Tickets table
        Ticket? ticket = await _ticketRepo.GetByKeyAsync(ticketKey, cancellationToken);
        if (ticket is null)
            return Problem(statusCode: 404, detail: "Ticket not found");

        // Resolve CustomerKey -> local Customer -> CustomerId
        var customerEntity = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerKey == ticket.CustomerKey, cancellationToken);

        if (customerEntity is null)
            return Problem(statusCode: 404, detail: "Customer not linked");

        // Store scope check (non-HQ users can only see their store's customers)
        if (!User.CanAccessStore(customerEntity.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        return Ok(new { customer_id = customerEntity.CustomerId });
    }
}
