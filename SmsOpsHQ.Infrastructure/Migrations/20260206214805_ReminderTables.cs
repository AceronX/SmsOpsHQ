using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReminderTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SMS_Excluded",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ExcludedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMS_Excluded", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SMS_Reminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketKey = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: true),
                    DueDate = table.Column<string>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ReminderType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    TwilioSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SentByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    StorePhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMS_Reminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SMS_Unsubscribed",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMS_Unsubscribed", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SMS_Excluded_Phone",
                table: "SMS_Excluded",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_SMS_Reminders_CreatedAt",
                table: "SMS_Reminders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SMS_Reminders_CustomerKey",
                table: "SMS_Reminders",
                column: "CustomerKey");

            migrationBuilder.CreateIndex(
                name: "IX_SMS_Reminders_TicketKey",
                table: "SMS_Reminders",
                column: "TicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_SMS_Unsubscribed_Phone",
                table: "SMS_Unsubscribed",
                column: "Phone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SMS_Excluded");

            migrationBuilder.DropTable(
                name: "SMS_Reminders");

            migrationBuilder.DropTable(
                name: "SMS_Unsubscribed");
        }
    }
}
