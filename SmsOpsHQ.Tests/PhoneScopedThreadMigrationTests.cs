using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmsOpsHQ.Infrastructure.Persistence;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class PhoneScopedThreadMigrationTests
{
    [Fact]
    public async Task AddPhoneScopedThreads_BackfillsDirections_AndPreservesUnresolvedLegacyRows()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsops_phase3_migration_{Guid.NewGuid():N}.db");
        try
        {
            DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using AppDbContext db = new(options);
            IMigrator migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260717115032_FixReviewDeliveryState");

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Stores (StoreId, StoreName, DefaultNumberId, IsActive)
                VALUES (1, 'Migration Store', 10, 1);
                INSERT INTO TwilioNumbers (NumberId, StoreId, PhoneE164, IsActive)
                VALUES (10, 1, '+15551110001', 1);

                INSERT INTO Threads (ThreadId, StoreId, Status, LastMessageAt, UnreadCount, CreatedAt)
                VALUES
                    (100, 1, 'Open', '2026-02-01T00:00:00Z', 0, '2026-01-01T00:00:00Z'),
                    (101, 1, 'Open', '2026-02-02T00:00:00Z', 0, '2026-01-01T00:00:00Z'),
                    (102, 1, 'Open', '2026-02-03T00:00:00Z', 0, '2026-01-01T00:00:00Z'),
                    (103, 1, 'Open', '2026-01-15T00:00:00Z', 0, '2026-01-01T00:00:00Z');

                INSERT INTO Messages
                    (MessageId, ThreadId, StoreId, Direction, FromE164, ToE164, Body, Category, Status, CreatedAt)
                VALUES
                    (1000, 100, 1, 'Outbound', '+15551110001', '(555) 222-0001', 'outbound', 'general', 'Sent', '2026-02-01T00:00:00Z'),
                    (1001, 101, 1, 'Inbound', '1-555-222-0002', '+15551110001', 'inbound', 'general', 'Received', '2026-02-02T00:00:00Z'),
                    (1002, 102, 1, 'Note', 'System', 'System', 'legacy note only', 'general', 'Internal', '2026-02-03T00:00:00Z'),
                    (1003, 103, 1, 'Outbound', '+15551110001', '+15552220001', 'duplicate legacy thread', 'general', 'Sent', '2026-01-15T00:00:00Z');
                """);

            await migrator.MigrateAsync();

            Dictionary<int, (int? NumberId, string? ContactPhone)> rows = new();
            DbConnection connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT ThreadId, TwilioNumberId, ContactPhoneE164 FROM Threads ORDER BY ThreadId";
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows[reader.GetInt32(0)] = (
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2));
            }

            Assert.Equal((10, "+15552220001"), rows[100]);
            Assert.Equal((10, "+15552220002"), rows[101]);
            Assert.Equal((null, null), rows[102]);
            Assert.Equal(10, rows[103].NumberId);
            Assert.Null(rows[103].ContactPhone);

            await db.Database.CloseConnectionAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
