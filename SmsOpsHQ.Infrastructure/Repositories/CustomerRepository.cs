using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of ICustomerRepository.
// Handles customer find/create, search, and partial updates.
public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db)
    {
        _db = db;
    }

    // Find an existing customer by phone within a store, or create one.
    public async Task<Customer> FindOrCreateAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default)
    {
        CustomerEntity? existing = await _db.Customers
            .FirstOrDefaultAsync(
                c => c.StoreId == storeId && c.PhoneE164 == phoneE164,
                cancellationToken);

        if (existing is not null)
            return MapToDomain(existing);

        CustomerEntity entity = new CustomerEntity
        {
            StoreId = storeId,
            PhoneE164 = phoneE164,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    public async Task<Customer?> GetByIdAsync(int storeId, int customerId,
        CancellationToken cancellationToken = default)
    {
        CustomerEntity? entity = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.StoreId == storeId && c.CustomerId == customerId,
                cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    // Search by first name, last name, or phone (case-insensitive LIKE).
    public async Task<List<Customer>> SearchAsync(int storeId, string query, int limit = 20,
        CancellationToken cancellationToken = default)
    {
        string q = query.ToLowerInvariant();

        List<CustomerEntity> entities = await _db.Customers
            .AsNoTracking()
            .Where(c => c.StoreId == storeId &&
                (
                    (c.FirstName != null && c.FirstName.ToLower().Contains(q)) ||
                    (c.LastName != null && c.LastName.ToLower().Contains(q)) ||
                    c.PhoneE164.Contains(q)
                ))
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    // Partial update: only non-null arguments are applied.
    public async Task UpdateAsync(int customerId, string? notes, string? firstName,
        string? lastName, string? tagsJson,
        CancellationToken cancellationToken = default)
    {
        CustomerEntity? entity = await _db.Customers
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

        if (entity is null)
            return;

        if (notes is not null) entity.Notes = notes;
        if (firstName is not null) entity.FirstName = firstName;
        if (lastName is not null) entity.LastName = lastName;
        if (tagsJson is not null) entity.TagsJson = tagsJson;

        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Customer MapToDomain(CustomerEntity entity)
    {
        return new Customer
        {
            CustomerId = entity.CustomerId,
            StoreId = entity.StoreId,
            PhoneE164 = entity.PhoneE164,
            CustomerKey = entity.CustomerKey,
            CellPhone = entity.CellPhone,
            HomePhone = entity.HomePhone,
            WorkPhone = entity.WorkPhone,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            Address = entity.Address,
            City = entity.City,
            State = entity.State,
            Zip = entity.Zip,
            SinceDate = entity.SinceDate,
            TagsJson = entity.TagsJson,
            Notes = entity.Notes,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
