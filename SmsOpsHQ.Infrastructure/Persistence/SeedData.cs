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

        // Create the database and schema from the model if it doesn't exist yet.
        await db.Database.EnsureCreatedAsync();

        // Schema upgrades for existing databases (EnsureCreated only builds fresh DBs).
        await UpgradeSchemaAsync(db, logger);

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

    private static async Task UpgradeSchemaAsync(AppDbContext db, ILogger logger)
    {
        // Add ReviewChannels table if missing.
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ReviewChannels (
                    ReviewChannelId INTEGER PRIMARY KEY AUTOINCREMENT,
                    StoreId         INTEGER NOT NULL,
                    PlatformName    TEXT    NOT NULL,
                    ReviewUrl       TEXT    NOT NULL,
                    SortOrder       INTEGER NOT NULL DEFAULT 0,
                    IsActive        INTEGER NOT NULL DEFAULT 1,
                    CreatedAt       TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (StoreId) REFERENCES Stores(StoreId)
                )");
            logger.LogInformation("Schema upgrade: ReviewChannels table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema upgrade: ReviewChannels table already exists or failed.");
        }

        // Add ReviewRequests table if missing.
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ReviewRequests (
                    ReviewRequestId INTEGER PRIMARY KEY AUTOINCREMENT,
                    StoreId         INTEGER NOT NULL,
                    CustomerId      INTEGER NOT NULL,
                    PhoneE164       TEXT    NOT NULL,
                    ReviewChannelId INTEGER NOT NULL,
                    TemplateId      INTEGER NOT NULL,
                    MessageBody     TEXT    NOT NULL,
                    TwilioSid       TEXT,
                    Status          TEXT    NOT NULL DEFAULT 'Sent',
                    SentAt          TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (StoreId) REFERENCES Stores(StoreId),
                    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId),
                    FOREIGN KEY (ReviewChannelId) REFERENCES ReviewChannels(ReviewChannelId),
                    FOREIGN KEY (TemplateId) REFERENCES Templates(TemplateId)
                )");
            logger.LogInformation("Schema upgrade: ReviewRequests table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema upgrade: ReviewRequests table already exists or failed.");
        }

        // Add Category column to Templates if missing.
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Templates ADD COLUMN Category TEXT DEFAULT 'General'");
            logger.LogInformation("Schema upgrade: Templates.Category column added.");
        }
        catch
        {
            // Column already exists — expected for upgraded DBs.
        }

        // Seed default review templates if none exist.
        // These are global templates (StoreId=0) with no real Store row,
        // so FK enforcement must be suspended for the insert.
        try
        {
            bool anyReviewTemplates = await db.Templates
                .AnyAsync(t => t.Category == "Review");

            if (!anyReviewTemplates)
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
                try
                {
                    var reviewTemplates = new List<TemplateEntity>
                    {
                        new() { StoreId = 0, Name = "Review - Feedback", Body = "Hi! Thanks for choosing {store}. We'd love your feedback: {link}", Category = "Review" },
                        new() { StoreId = 0, Name = "Review - Opinion", Body = "Your opinion matters to us! Please share your experience: {link} - {store}", Category = "Review" },
                        new() { StoreId = 0, Name = "Review - Great Experience", Body = "Had a great experience at {store}? Let others know! {link}", Category = "Review" },
                        new() { StoreId = 0, Name = "Review - Enjoyed Visit", Body = "We hope you enjoyed your visit to {store}! A quick review would mean a lot: {link}", Category = "Review" },
                        new() { StoreId = 0, Name = "Review - Valued Customer", Body = "Thanks for being a valued customer of {store}! Share your thoughts: {link}", Category = "Review" }
                    };

                    db.Templates.AddRange(reviewTemplates);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Seed complete: 5 review templates created.");
                }
                finally
                {
                    await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed review templates.");
        }

        // CustomerPhones: MatchType + IsDirect for identity resolution (legacy rows used PhoneType only).
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CustomerPhones ADD COLUMN MatchType TEXT");
            logger.LogInformation("Schema upgrade: CustomerPhones.MatchType added.");
        }
        catch
        {
            // Column may already exist.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE CustomerPhones ADD COLUMN IsDirect INTEGER NOT NULL DEFAULT 0");
            logger.LogInformation("Schema upgrade: CustomerPhones.IsDirect added.");
        }
        catch
        {
            // Column may already exist.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE CustomerPhones SET MatchType = CASE PhoneType
                    WHEN 'ResPhone' THEN 'direct_res_phone'
                    WHEN 'BusPhone' THEN 'direct_bus_phone'
                    WHEN 'TicketNotes' THEN 'ticket_note_reference'
                    ELSE 'note_reference' END
                WHERE MatchType IS NULL OR TRIM(MatchType) = ''
                """);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE CustomerPhones SET IsDirect = CASE
                    WHEN PhoneType IN ('ResPhone', 'BusPhone') THEN 1
                    ELSE 0 END
                """);
            logger.LogInformation("Schema upgrade: CustomerPhones MatchType/IsDirect backfilled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema upgrade: CustomerPhones MatchType/IsDirect backfill skipped.");
        }

        // Review automation watermark (new XPD tickets → review SMS).
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ReviewAutomationState (
                    StateId           INTEGER PRIMARY KEY,
                    LastMaxTicketKey  INTEGER NULL
                )");
            logger.LogInformation("Schema upgrade: ReviewAutomationState table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema upgrade: ReviewAutomationState table already exists or failed.");
        }
    }
}
