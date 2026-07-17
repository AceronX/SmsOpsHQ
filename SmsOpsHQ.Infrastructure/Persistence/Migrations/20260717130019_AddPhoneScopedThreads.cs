using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneScopedThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPhoneE164",
                table: "Threads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            // Backfill from the newest non-note message in each legacy thread.
            // The temp table keeps the normalization deterministic and makes it
            // possible to resolve both sides of the conversation independently.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE _PhoneScopedThreadBackfill
                (
                    ThreadId INTEGER NOT NULL PRIMARY KEY,
                    ContactDigits TEXT NULL,
                    StoreDigits TEXT NULL
                );

                WITH RankedMessages AS
                (
                    SELECT
                        ThreadId,
                        Direction,
                        FromE164,
                        ToE164,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY ThreadId
                            ORDER BY CreatedAt DESC, MessageId DESC
                        ) AS RowNumber
                    FROM Messages
                    WHERE Direction <> 'Note'
                )
                INSERT INTO _PhoneScopedThreadBackfill (ThreadId, ContactDigits, StoreDigits)
                SELECT
                    ThreadId,
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        TRIM(CASE
                            WHEN Direction = 'Outbound' THEN ToE164
                            WHEN Direction = 'Inbound' THEN FromE164
                            ELSE NULL
                        END),
                        '+', ''), '(', ''), ')', ''), '-', ''), '.', ''), ' ', ''), '/', ''), CHAR(9), ''),
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        TRIM(CASE
                            WHEN Direction = 'Outbound' THEN FromE164
                            WHEN Direction = 'Inbound' THEN ToE164
                            ELSE NULL
                        END),
                        '+', ''), '(', ''), ')', ''), '-', ''), '.', ''), ' ', ''), '/', ''), CHAR(9), '')
                FROM RankedMessages
                WHERE RowNumber = 1;

                UPDATE Threads
                SET ContactPhoneE164 =
                    (
                        SELECT CASE
                            WHEN ContactDigits NOT GLOB '*[^0-9]*' AND LENGTH(ContactDigits) = 10
                                THEN '+1' || ContactDigits
                            WHEN ContactDigits NOT GLOB '*[^0-9]*'
                                 AND LENGTH(ContactDigits) = 11
                                 AND SUBSTR(ContactDigits, 1, 1) = '1'
                                THEN '+' || ContactDigits
                            ELSE NULL
                        END
                        FROM _PhoneScopedThreadBackfill b
                        WHERE b.ThreadId = Threads.ThreadId
                    ),
                    TwilioNumberId =
                    (
                        SELECT n.NumberId
                        FROM _PhoneScopedThreadBackfill b
                        JOIN TwilioNumbers n
                          ON n.StoreId = Threads.StoreId
                         AND n.IsActive = 1
                         AND n.PhoneE164 = CASE
                            WHEN b.StoreDigits NOT GLOB '*[^0-9]*' AND LENGTH(b.StoreDigits) = 10
                                THEN '+1' || b.StoreDigits
                            WHEN b.StoreDigits NOT GLOB '*[^0-9]*'
                                 AND LENGTH(b.StoreDigits) = 11
                                 AND SUBSTR(b.StoreDigits, 1, 1) = '1'
                                THEN '+' || b.StoreDigits
                            ELSE NULL
                         END
                        WHERE b.ThreadId = Threads.ThreadId
                        ORDER BY n.NumberId
                        LIMIT 1
                    )
                WHERE ThreadId IN (SELECT ThreadId FROM _PhoneScopedThreadBackfill);

                DROP TABLE _PhoneScopedThreadBackfill;

                -- Multiple legacy rows can represent the same exact conversation.
                -- Preserve every row, but leave older duplicates unresolved so the
                -- new unique index can protect all newly-created conversations.
                WITH RankedThreads AS
                (
                    SELECT
                        ThreadId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY StoreId, TwilioNumberId, ContactPhoneE164
                            ORDER BY COALESCE(LastMessageAt, CreatedAt) DESC, ThreadId DESC
                        ) AS RowNumber
                    FROM Threads
                    WHERE ContactPhoneE164 IS NOT NULL
                      AND TwilioNumberId IS NOT NULL
                )
                UPDATE Threads
                SET ContactPhoneE164 = NULL
                WHERE ThreadId IN
                (
                    SELECT ThreadId
                    FROM RankedThreads
                    WHERE RowNumber > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Threads_ContactPhoneE164",
                table: "Threads",
                column: "ContactPhoneE164");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_StoreId_TwilioNumberId_ContactPhoneE164",
                table: "Threads",
                columns: new[] { "StoreId", "TwilioNumberId", "ContactPhoneE164" },
                unique: true,
                filter: "ContactPhoneE164 IS NOT NULL AND TwilioNumberId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Threads_ContactPhoneE164",
                table: "Threads");

            migrationBuilder.DropIndex(
                name: "IX_Threads_StoreId_TwilioNumberId_ContactPhoneE164",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "ContactPhoneE164",
                table: "Threads");
        }
    }
}
