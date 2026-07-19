using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLateTicketPullList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LateTicketPulls",
                columns: table => new
                {
                    LateTicketPullId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketKey = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PulledByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PulledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LateTicketPulls", x => x.LateTicketPullId);
                    table.ForeignKey(
                        name: "FK_LateTicketPulls_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LateTicketPulls_Users_PulledByUserId",
                        column: x => x.PulledByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LateTicketPulls_PulledAtUtc",
                table: "LateTicketPulls",
                column: "PulledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LateTicketPulls_PulledByUserId",
                table: "LateTicketPulls",
                column: "PulledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LateTicketPulls_StoreId_CustomerKey",
                table: "LateTicketPulls",
                columns: new[] { "StoreId", "CustomerKey" });

            migrationBuilder.CreateIndex(
                name: "IX_LateTicketPulls_StoreId_TicketKey",
                table: "LateTicketPulls",
                columns: new[] { "StoreId", "TicketKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LateTicketPulls");
        }
    }
}
