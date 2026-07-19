using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class LateTicketPullRepositoryTests
{
    [Fact]
    public async Task Pull_SurvivesContextRestartAndXpdTicketReplacement_ThenRestoresIdempotently()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"smsops_pull_list_{Guid.NewGuid():N}.db");
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

        try
        {
            int storeId;
            int userId;
            const int customerKey = 920001;
            const int ticketKey = 930001;

            await using (AppDbContext firstContext = new(options))
            {
                await firstContext.Database.MigrateAsync();
                StoreEntity store = new()
                {
                    StoreName = "Pull Restart Store",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                firstContext.Stores.Add(store);
                await firstContext.SaveChangesAsync();
                storeId = store.StoreId;

                UserEntity user = new()
                {
                    StoreId = storeId,
                    Username = "pull-restart-user",
                    PasswordHash = "hash",
                    Role = "StoreAdmin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                firstContext.Users.Add(user);
                firstContext.Customers.Add(new CustomerEntity
                {
                    StoreId = storeId,
                    CustomerKey = customerKey,
                    PhoneE164 = "+17185559201",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                firstContext.Tickets.Add(NewLateTicket(ticketKey, customerKey));
                await firstContext.SaveChangesAsync();
                userId = user.UserId;

                LateTicketPullRepository repository = new(firstContext);
                LateTicketPull first = await repository.PullAsync(
                    storeId,
                    ticketKey,
                    customerKey,
                    "Manager review",
                    userId);
                LateTicketPull repeated = await repository.PullAsync(
                    storeId,
                    ticketKey,
                    customerKey,
                    "Ignored duplicate reason",
                    userId);

                Assert.Equal(first.LateTicketPullId, repeated.LateTicketPullId);
                Assert.Equal("Manager review", repeated.Reason);

                TicketEntity mirroredTicket = await firstContext.Tickets.SingleAsync(
                    ticket => ticket.Key == ticketKey);
                firstContext.Tickets.Remove(mirroredTicket);
                await firstContext.SaveChangesAsync();
                LateTicketPull repeatedWhileMirrorMissing = await repository.PullAsync(
                    storeId,
                    ticketKey,
                    customerKey,
                    null,
                    userId);
                Assert.Equal(first.LateTicketPullId, repeatedWhileMirrorMissing.LateTicketPullId);
                firstContext.Tickets.Add(NewLateTicket(ticketKey, customerKey));
                await firstContext.SaveChangesAsync();
            }

            await using AppDbContext restartedContext = new(options);
            LateTicketPullRepository restartedRepository = new(restartedContext);
            LateTicketPull saved = Assert.Single(
                await restartedRepository.GetByStoreAsync(storeId));
            Assert.Equal(ticketKey, saved.TicketKey);
            Assert.Equal(customerKey, saved.CustomerKey);

            await restartedRepository.RestoreAsync(storeId, ticketKey);
            await restartedRepository.RestoreAsync(storeId, ticketKey);
            Assert.Empty(await restartedRepository.GetByStoreAsync(storeId));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static TicketEntity NewLateTicket(int ticketKey, int customerKey) => new()
    {
        Key = ticketKey,
        CustomerKey = customerKey,
        TransNo = ticketKey,
        Type = 1,
        Active = 1,
        DueDate = "2000-01-01",
        Amount = 100,
        CurrentBalance = 50
    };
}
