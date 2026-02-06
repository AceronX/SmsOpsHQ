using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Persistence;

// Seeds initial data (one Store, one HQ admin user) for development.
// Idempotent: skips if the admin user already exists.
public static class SeedData
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "password";

    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SmsOpsHQ.Infrastructure.SeedData");

        // Apply any pending migrations automatically in development.
        await db.Database.MigrateAsync();

        bool adminExists = await db.Users
            .AnyAsync(u => u.Username == DefaultAdminUsername);

        if (adminExists)
        {
            logger.LogInformation("Seed skipped: admin user already exists.");
            return;
        }

        // Seed one store.
        StoreEntity store = new StoreEntity
        {
            StoreName = "HQ",
            Address = "123 Main St",
            City = "New York",
            State = "NY",
            Zip = "10001",
            IsActive = true
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        // Seed one HQ admin user (StoreId = null for HQ-level access).
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword);
        UserEntity adminUser = new UserEntity
        {
            StoreId = null,
            FullName = "HQ Administrator",
            Username = DefaultAdminUsername,
            PasswordHash = passwordHash,
            Role = "HQAdmin",
            IsActive = true
        };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seed complete: Store '{StoreName}' (Id={StoreId}) and user '{Username}' created.",
            store.StoreName, store.StoreId, adminUser.Username);
    }
}
