using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SmsOpsHQ.Infrastructure.Persistence;

internal static class DatabaseMigrator
{
    /// <summary>Baseline migration stamped onto pre-migration SQLite databases.</summary>
    public const string BaselineMigrationId = "20260526211622_InitialCreate";

    public static async Task MigrateAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        bool usersTableExists = await TableExistsAsync(db, "Users", ct);
        bool historyExists = await MigrationHistoryTableExistsAsync(db, ct);
        IEnumerable<string> applied = historyExists
            ? await db.Database.GetAppliedMigrationsAsync(ct)
            : Array.Empty<string>();

        if (usersTableExists && !applied.Any())
        {
            logger.LogInformation(
                "Existing database detected without EF migration history. Applying legacy schema patches and stamping baseline {MigrationId}.",
                BaselineMigrationId);
            await LegacySchemaPatcher.ApplyAsync(db, logger, ct);
            await StampBaselineMigrationAsync(db, logger, ct);
        }

        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Database migrations applied.");
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName, CancellationToken ct)
    {
        int count = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = {0}
                """,
                tableName)
            .FirstOrDefaultAsync(ct);
        return count > 0;
    }

    private static async Task<bool> MigrationHistoryTableExistsAsync(AppDbContext db, CancellationToken ct)
    {
        int count = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = '__EFMigrationsHistory'
                """)
            .FirstOrDefaultAsync(ct);
        return count > 0;
    }

    private static async Task StampBaselineMigrationAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
                MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                ProductVersion TEXT NOT NULL
            )
            """, ct);

        string productVersion = typeof(DbContext).Assembly
            .GetName()
            .Version?
            .ToString(3) ?? "8.0.0";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES ({BaselineMigrationId}, {productVersion})
            """,
            ct);

        logger.LogInformation("Stamped baseline migration {MigrationId}.", BaselineMigrationId);
    }
}
