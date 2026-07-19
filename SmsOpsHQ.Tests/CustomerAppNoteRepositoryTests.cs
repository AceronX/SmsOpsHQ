using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class CustomerAppNoteRepositoryTests
{
    [Fact]
    public async Task CreatedNote_SurvivesDatabaseContextRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"smsops_app_notes_{Guid.NewGuid():N}.db");
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

        try
        {
            int storeId;
            int userId;
            await using (AppDbContext firstContext = new(options))
            {
                await firstContext.Database.MigrateAsync();
                StoreEntity store = new()
                {
                    StoreName = "Restart Test Store",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                firstContext.Stores.Add(store);
                await firstContext.SaveChangesAsync();
                storeId = store.StoreId;

                UserEntity user = new()
                {
                    StoreId = storeId,
                    Username = "restart-note-user",
                    PasswordHash = "hash",
                    Role = "StoreAdmin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                firstContext.Users.Add(user);
                firstContext.Customers.Add(new CustomerEntity
                {
                    StoreId = storeId,
                    CustomerKey = 910001,
                    PhoneE164 = "+17185559101",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await firstContext.SaveChangesAsync();
                userId = user.UserId;

                CustomerAppNoteRepository repository = new(firstContext);
                await repository.CreateAsync(storeId, 910001, "Persistent app note", userId);
            }

            await using AppDbContext restartedContext = new(options);
            CustomerAppNoteRepository restartedRepository = new(restartedContext);
            IReadOnlyList<CustomerAppNote> notes = await restartedRepository.GetByCustomerAsync(
                storeId,
                910001);

            CustomerAppNote saved = Assert.Single(notes);
            Assert.Equal("Persistent app note", saved.Content);
            Assert.Equal("restart-note-user", saved.CreatedByUsername);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
