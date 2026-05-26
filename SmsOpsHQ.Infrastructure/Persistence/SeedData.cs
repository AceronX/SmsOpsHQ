using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Persistence;

// Seeds initial data (one HQ admin user only). No store is created automatically.
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

        await DatabaseMigrator.MigrateAsync(db, logger);

        bool anyUserExists = await db.Users.AnyAsync();

        if (anyUserExists)
        {
            logger.LogInformation("Seed skipped: at least one user already exists.");
            return;
        }

        // Seed one HQ admin user only (StoreId = null for HQ-level access). No store is auto-created.
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword);
        UserEntity adminUser = new UserEntity
        {
            StoreId = null,
            TwilioNumberId = null,
            Username = DefaultAdminUsername,
            PasswordHash = passwordHash,
            Role = "HQAdmin",
            IsActive = true
        };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seed complete: HQ admin user '{Username}' created. No store was created; create stores via API or admin UI.",
            adminUser.Username);
    }
}
