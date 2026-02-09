using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoreMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    IPAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_AuditLog_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AuditLog_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: true),
                    CellPhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    HomePhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    WorkPhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SinceDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_Customers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OptOuts",
                columns: table => new
                {
                    OptOutId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OptOutDate = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptOuts", x => x.OptOutId);
                    table.ForeignKey(
                        name: "FK_OptOuts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuarantinedMessages",
                columns: table => new
                {
                    QuarantineId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    MediaJson = table.Column<string>(type: "TEXT", nullable: true),
                    TwilioSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    QuarantineReason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    QuarantinedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuarantinedMessages", x => x.QuarantineId);
                    table.ForeignKey(
                        name: "FK_QuarantinedMessages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuarantinedMessages_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Hotkey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.TemplateId);
                    table.ForeignKey(
                        name: "FK_Templates_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Templates_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Threads",
                columns: table => new
                {
                    ThreadId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TwilioNumberId = table.Column<int>(type: "INTEGER", nullable: true),
                    IdentityId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Open"),
                    AssignedToUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnreadCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Threads", x => x.ThreadId);
                    table.ForeignKey(
                        name: "FK_Threads_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Threads_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TwilioNumbers",
                columns: table => new
                {
                    NumberId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TwilioSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MessagingServiceSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwilioNumbers", x => x.NumberId);
                    table.ForeignKey(
                        name: "FK_TwilioNumbers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    StorePhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FromE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    MediaJson = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "general"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TwilioSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SentByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_Messages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "Threads",
                        principalColumn: "ThreadId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_CreatedAt",
                table: "AuditLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_StoreId",
                table: "AuditLog",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerKey",
                table: "Customers",
                column: "CustomerKey");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FirstName_LastName",
                table: "Customers",
                columns: new[] { "FirstName", "LastName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PhoneE164",
                table: "Customers",
                column: "PhoneE164");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId",
                table: "Customers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Category",
                table: "Messages",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_CreatedAt",
                table: "Messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SentByUserId",
                table: "Messages",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_StoreId",
                table: "Messages",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ThreadId",
                table: "Messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TwilioSid",
                table: "Messages",
                column: "TwilioSid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptOuts_StoreId_PhoneE164",
                table: "OptOuts",
                columns: new[] { "StoreId", "PhoneE164" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedMessages_ReviewedByUserId",
                table: "QuarantinedMessages",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedMessages_StoreId",
                table: "QuarantinedMessages",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_CreatedByUserId",
                table: "Templates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_StoreId",
                table: "Templates",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_AssignedToUserId",
                table: "Threads",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_IdentityId",
                table: "Threads",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_LastMessageAt",
                table: "Threads",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_Status",
                table: "Threads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_StoreId",
                table: "Threads",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_TwilioNumbers_PhoneE164",
                table: "TwilioNumbers",
                column: "PhoneE164",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TwilioNumbers_StoreId",
                table: "TwilioNumbers",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "OptOuts");

            migrationBuilder.DropTable(
                name: "QuarantinedMessages");

            migrationBuilder.DropTable(
                name: "Templates");

            migrationBuilder.DropTable(
                name: "TwilioNumbers");

            migrationBuilder.DropTable(
                name: "Threads");
        }
    }
}
