using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for CustomerRepository against an in-memory SQLite database.
public class CustomerRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CustomerRepository _repo;
    private readonly int _storeId;

    public CustomerRepositoryTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        StoreEntity store = new StoreEntity { StoreName = "Test Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        _repo = new CustomerRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── FindOrCreateAsync ────────────────────────────────────────────

    [Fact]
    public async Task FindOrCreateAsync_NewPhone_CreatesCustomer()
    {
        Customer customer = await _repo.FindOrCreateAsync(_storeId, "+17185551234");

        Assert.True(customer.CustomerId > 0);
        Assert.Equal(_storeId, customer.StoreId);
        Assert.Equal("+17185551234", customer.PhoneE164);
    }

    [Fact]
    public async Task FindOrCreateAsync_ExistingPhone_ReturnsExisting()
    {
        Customer created = await _repo.FindOrCreateAsync(_storeId, "+17185551234");
        Customer found = await _repo.FindOrCreateAsync(_storeId, "+17185551234");

        Assert.Equal(created.CustomerId, found.CustomerId);
    }

    [Fact]
    public async Task FindOrCreateAsync_DifferentStores_CreatesSeparate()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        Customer c1 = await _repo.FindOrCreateAsync(_storeId, "+17185551234");
        Customer c2 = await _repo.FindOrCreateAsync(store2.StoreId, "+17185551234");

        Assert.NotEqual(c1.CustomerId, c2.CustomerId);
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsCustomer()
    {
        Customer created = await _repo.FindOrCreateAsync(_storeId, "+17185551234");
        Customer? found = await _repo.GetByIdAsync(_storeId, created.CustomerId);

        Assert.NotNull(found);
        Assert.Equal("+17185551234", found.PhoneE164);
    }

    [Fact]
    public async Task GetByIdAsync_WrongStore_ReturnsNull()
    {
        Customer created = await _repo.FindOrCreateAsync(_storeId, "+17185551234");
        Customer? found = await _repo.GetByIdAsync(99999, created.CustomerId);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByIdAsync_Nonexistent_ReturnsNull()
    {
        Customer? found = await _repo.GetByIdAsync(_storeId, 99999);
        Assert.Null(found);
    }

    // ── SearchAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ByFirstName_ReturnsMatches()
    {
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+11111111111",
            FirstName = "John", LastName = "Doe"
        });
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+12222222222",
            FirstName = "Jane", LastName = "Doe"
        });
        _db.SaveChanges();

        List<Customer> results = await _repo.SearchAsync(_storeId, "john");

        Assert.Single(results);
        Assert.Equal("John", results[0].FirstName);
    }

    [Fact]
    public async Task SearchAsync_ByLastName_ReturnsMatches()
    {
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+11111111111",
            FirstName = "John", LastName = "Doe"
        });
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+12222222222",
            FirstName = "Jane", LastName = "Smith"
        });
        _db.SaveChanges();

        List<Customer> results = await _repo.SearchAsync(_storeId, "doe");

        Assert.Single(results);
        Assert.Equal("John", results[0].FirstName);
    }

    [Fact]
    public async Task SearchAsync_ByPhone_ReturnsMatches()
    {
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+17185551234",
            FirstName = "John"
        });
        _db.SaveChanges();

        List<Customer> results = await _repo.SearchAsync(_storeId, "7185551234");

        Assert.Single(results);
        Assert.Equal("John", results[0].FirstName);
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
        {
            _db.Customers.Add(new CustomerEntity
            {
                StoreId = _storeId, PhoneE164 = $"+1000000000{i}",
                FirstName = "Test"
            });
        }
        _db.SaveChanges();

        List<Customer> results = await _repo.SearchAsync(_storeId, "test", limit: 3);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SearchAsync_StoreIsolation()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        _db.Customers.Add(new CustomerEntity
        {
            StoreId = _storeId, PhoneE164 = "+11111111111",
            FirstName = "Alice"
        });
        _db.Customers.Add(new CustomerEntity
        {
            StoreId = store2.StoreId, PhoneE164 = "+12222222222",
            FirstName = "Alice"
        });
        _db.SaveChanges();

        List<Customer> results = await _repo.SearchAsync(_storeId, "alice");
        Assert.Single(results);
    }

    // ── UpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PartialUpdate_OnlyChangesProvidedFields()
    {
        Customer created = await _repo.FindOrCreateAsync(_storeId, "+17185551234");

        await _repo.UpdateAsync(created.CustomerId,
            notes: "VIP customer",
            firstName: "John",
            lastName: null,
            tagsJson: null);

        Customer? updated = await _repo.GetByIdAsync(_storeId, created.CustomerId);
        Assert.NotNull(updated);
        Assert.Equal("VIP customer", updated.Notes);
        Assert.Equal("John", updated.FirstName);
        Assert.Null(updated.LastName); // unchanged (was null, passed null)
    }

    [Fact]
    public async Task UpdateAsync_AllFields()
    {
        Customer created = await _repo.FindOrCreateAsync(_storeId, "+17185551234");

        await _repo.UpdateAsync(created.CustomerId,
            notes: "Note",
            firstName: "Jane",
            lastName: "Doe",
            tagsJson: "[\"vip\"]");

        Customer? updated = await _repo.GetByIdAsync(_storeId, created.CustomerId);
        Assert.NotNull(updated);
        Assert.Equal("Note", updated.Notes);
        Assert.Equal("Jane", updated.FirstName);
        Assert.Equal("Doe", updated.LastName);
        Assert.Equal("[\"vip\"]", updated.TagsJson);
    }

    [Fact]
    public async Task UpdateAsync_Nonexistent_DoesNotThrow()
    {
        // Should silently do nothing
        await _repo.UpdateAsync(99999, notes: "test", firstName: null, lastName: null, tagsJson: null);
    }
}
