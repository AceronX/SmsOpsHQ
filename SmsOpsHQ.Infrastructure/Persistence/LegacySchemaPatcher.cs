using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Persistence;

/// <summary>
/// Idempotent schema fixes for databases created before EF Core migrations.
/// Safe to run on every startup until the baseline migration is recorded.
/// </summary>
internal static class LegacySchemaPatcher
{
    public static async Task ApplyAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await EnsureReviewTablesAsync(db, logger, ct);
        await EnsureTemplatesCategoryAsync(db, logger, ct);
        await SeedReviewTemplatesAsync(db, logger, ct);
        await EnsureCustomerPhonesColumnsAsync(db, logger, ct);
        await EnsureReviewAutomationStateAsync(db, logger, ct);
    }

    private static async Task EnsureReviewTablesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
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
                )", ct);
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
                )", ct);
            logger.LogInformation("Legacy schema patch: ReviewChannels / ReviewRequests ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Legacy schema patch: Review tables step failed.");
        }
    }

    private static async Task EnsureTemplatesCategoryAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Templates ADD COLUMN Category TEXT DEFAULT 'General'", ct);
            logger.LogInformation("Legacy schema patch: Templates.Category added.");
        }
        catch
        {
            // Column already exists.
        }
    }

    private static async Task SeedReviewTemplatesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        try
        {
            bool anyReviewTemplates = await db.Templates.AnyAsync(t => t.Category == "Review", ct);
            if (anyReviewTemplates) return;

            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", ct);
            try
            {
                List<TemplateEntity> reviewTemplates =
                [
                    new() { StoreId = 0, Name = "Review - Feedback", Body = "Hi! Thanks for choosing {store}. We'd love your feedback: {link}", Category = "Review" },
                    new() { StoreId = 0, Name = "Review - Opinion", Body = "Your opinion matters to us! Please share your experience: {link} - {store}", Category = "Review" },
                    new() { StoreId = 0, Name = "Review - Great Experience", Body = "Had a great experience at {store}? Let others know! {link}", Category = "Review" },
                    new() { StoreId = 0, Name = "Review - Enjoyed Visit", Body = "We hope you enjoyed your visit to {store}! A quick review would mean a lot: {link}", Category = "Review" },
                    new() { StoreId = 0, Name = "Review - Valued Customer", Body = "Thanks for being a valued customer of {store}! Share your thoughts: {link}", Category = "Review" }
                ];
                db.Templates.AddRange(reviewTemplates);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Legacy schema patch: default review templates seeded.");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON", ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Legacy schema patch: review template seed failed.");
        }
    }

    private static async Task EnsureCustomerPhonesColumnsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CustomerPhones ADD COLUMN MatchType TEXT", ct);
        }
        catch { /* exists */ }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE CustomerPhones ADD COLUMN IsDirect INTEGER NOT NULL DEFAULT 0", ct);
        }
        catch { /* exists */ }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE CustomerPhones SET MatchType = CASE PhoneType
                    WHEN 'ResPhone' THEN 'direct_res_phone'
                    WHEN 'BusPhone' THEN 'direct_bus_phone'
                    WHEN 'TicketNotes' THEN 'ticket_note_reference'
                    ELSE 'note_reference' END
                WHERE MatchType IS NULL OR TRIM(MatchType) = ''
                """, ct);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE CustomerPhones SET IsDirect = CASE
                    WHEN PhoneType IN ('ResPhone', 'BusPhone') THEN 1
                    ELSE 0 END
                """, ct);
            logger.LogInformation("Legacy schema patch: CustomerPhones MatchType/IsDirect backfilled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Legacy schema patch: CustomerPhones backfill skipped.");
        }
    }

    private static async Task EnsureReviewAutomationStateAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ReviewAutomationState (
                    StateId           INTEGER PRIMARY KEY,
                    LastMaxTicketKey  INTEGER NULL
                )", ct);
            logger.LogInformation("Legacy schema patch: ReviewAutomationState ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Legacy schema patch: ReviewAutomationState step failed.");
        }
    }
}
