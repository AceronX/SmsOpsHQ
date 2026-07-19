using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

public interface ICustomerAppNoteRepository
{
    Task<IReadOnlyList<CustomerAppNote>> GetByCustomerAsync(
        int storeId,
        int customerKey,
        CancellationToken cancellationToken = default);

    Task<CustomerAppNote> CreateAsync(
        int storeId,
        int customerKey,
        string content,
        int createdByUserId,
        CancellationToken cancellationToken = default);
}
