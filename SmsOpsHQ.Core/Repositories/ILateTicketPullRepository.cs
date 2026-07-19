using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

public interface ILateTicketPullRepository
{
    Task<LateTicketPull?> GetAsync(
        int storeId,
        int ticketKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LateTicketPull>> GetByStoreAsync(
        int storeId,
        CancellationToken cancellationToken = default);

    Task<LateTicketPull> PullAsync(
        int storeId,
        int ticketKey,
        int customerKey,
        string? reason,
        int pulledByUserId,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        int storeId,
        int ticketKey,
        CancellationToken cancellationToken = default);
}
