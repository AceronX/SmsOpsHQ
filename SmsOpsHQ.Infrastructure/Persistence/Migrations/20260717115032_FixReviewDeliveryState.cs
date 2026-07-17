using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixReviewDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ReviewRequests",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Accepted",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldDefaultValue: "Sent");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "ReviewRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ReviewRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ReviewRequests",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderStatus",
                table: "ReviewRequests",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_TwilioSid",
                table: "ReviewRequests",
                column: "TwilioSid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReviewRequests_TwilioSid",
                table: "ReviewRequests");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "ReviewRequests");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ReviewRequests");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ReviewRequests");

            migrationBuilder.DropColumn(
                name: "ProviderStatus",
                table: "ReviewRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ReviewRequests",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Sent",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldDefaultValue: "Accepted");
        }
    }
}
