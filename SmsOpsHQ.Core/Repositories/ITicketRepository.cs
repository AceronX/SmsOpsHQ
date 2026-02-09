using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for XPD ticket mirror data.
public interface ITicketRepository
{
    // Get all tickets for a customer by XPD customer key.
    Task<List<Ticket>> GetByCustomerKeyAsync(int customerKey,
        CancellationToken cancellationToken = default);

    // Get tickets for multiple customer keys (identity resolution may return several).
    Task<List<Ticket>> GetByCustomerKeysAsync(List<int> customerKeys,
        CancellationToken cancellationToken = default);

    // Get a single ticket by XPD ticket key.
    Task<Ticket?> GetByKeyAsync(int ticketKey,
        CancellationToken cancellationToken = default);
}
