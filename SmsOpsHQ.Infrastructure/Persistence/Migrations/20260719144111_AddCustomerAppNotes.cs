using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAppNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAppNotes",
                columns: table => new
                {
                    CustomerAppNoteId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAppNotes", x => x.CustomerAppNoteId);
                    table.ForeignKey(
                        name: "FK_CustomerAppNotes_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAppNotes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAppNotes_CreatedAtUtc",
                table: "CustomerAppNotes",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAppNotes_CreatedByUserId",
                table: "CustomerAppNotes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAppNotes_StoreId_CustomerKey",
                table: "CustomerAppNotes",
                columns: new[] { "StoreId", "CustomerKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAppNotes");
        }
    }
}
