using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

public sealed class CustomerAppNoteRepository : ICustomerAppNoteRepository
{
    private readonly AppDbContext _db;

    public CustomerAppNoteRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerAppNote>> GetByCustomerAsync(
        int storeId,
        int customerKey,
        CancellationToken cancellationToken = default)
    {
        return await (
                from note in _db.CustomerAppNotes.AsNoTracking()
                join user in _db.Users.AsNoTracking()
                    on note.CreatedByUserId equals user.UserId
                where note.StoreId == storeId && note.CustomerKey == customerKey
                orderby note.CreatedAtUtc, note.CustomerAppNoteId
                select new CustomerAppNote
                {
                    CustomerAppNoteId = note.CustomerAppNoteId,
                    StoreId = note.StoreId,
                    CustomerKey = note.CustomerKey,
                    Content = note.Content,
                    CreatedByUserId = note.CreatedByUserId,
                    CreatedByUsername = user.Username,
                    CreatedAtUtc = note.CreatedAtUtc,
                    UpdatedAtUtc = note.UpdatedAtUtc
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerAppNote> CreateAsync(
        int storeId,
        int customerKey,
        string content,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        DateTime createdAtUtc = DateTime.UtcNow;
        CustomerAppNoteEntity entity = new()
        {
            StoreId = storeId,
            CustomerKey = customerKey,
            Content = content,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc
        };

        _db.CustomerAppNotes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        string username = await _db.Users
            .Where(user => user.UserId == createdByUserId)
            .Select(user => user.Username)
            .SingleAsync(cancellationToken);

        return new CustomerAppNote
        {
            CustomerAppNoteId = entity.CustomerAppNoteId,
            StoreId = entity.StoreId,
            CustomerKey = entity.CustomerKey,
            Content = entity.Content,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedByUsername = username,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}
