using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class XpdMirrorTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XPD_CustomerPhones",
                columns: table => new
                {
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneNormalized = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PhoneType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PhoneOriginal = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPD_CustomerPhones", x => new { x.CustomerKey, x.PhoneNormalized, x.PhoneType });
                });

            migrationBuilder.CreateTable(
                name: "XPD_Customers",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastName = table.Column<string>(type: "TEXT", nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", nullable: true),
                    MiddleName = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    City = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: true),
                    Zip = table.Column<string>(type: "TEXT", nullable: true),
                    ResPhone = table.Column<string>(type: "TEXT", nullable: true),
                    BusPhone = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    DOB = table.Column<string>(type: "TEXT", nullable: true),
                    SSN = table.Column<string>(type: "TEXT", nullable: true),
                    IDNo = table.Column<string>(type: "TEXT", nullable: true),
                    IDIssueState = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    FirstTransaction = table.Column<string>(type: "TEXT", nullable: true),
                    LastTransaction = table.Column<string>(type: "TEXT", nullable: true),
                    Warning = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPD_Customers", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "XPD_Items",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketKey = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintedDetail = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryCode = table.Column<string>(type: "TEXT", nullable: true),
                    SerialNo = table.Column<string>(type: "TEXT", nullable: true),
                    Cost = table.Column<double>(type: "REAL", nullable: true),
                    ItemStatus = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Brand = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<string>(type: "TEXT", nullable: true),
                    Metal = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPD_Items", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "XPD_PawnPayments",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketKey = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentDate = table.Column<string>(type: "TEXT", nullable: true),
                    PawnPmtType = table.Column<int>(type: "INTEGER", nullable: true),
                    PaymentStatus = table.Column<string>(type: "TEXT", nullable: true),
                    TotalDueAmount = table.Column<double>(type: "REAL", nullable: true),
                    NetDueAmount = table.Column<double>(type: "REAL", nullable: true),
                    NetPaymentAmount = table.Column<double>(type: "REAL", nullable: true),
                    Cash = table.Column<double>(type: "REAL", nullable: true),
                    Check_ = table.Column<double>(type: "REAL", nullable: true),
                    CreditCard = table.Column<double>(type: "REAL", nullable: true),
                    DebitCard = table.Column<double>(type: "REAL", nullable: true),
                    InterestChargePaid = table.Column<double>(type: "REAL", nullable: true),
                    ServiceChargePaid = table.Column<double>(type: "REAL", nullable: true),
                    PrincipalPaid = table.Column<double>(type: "REAL", nullable: true),
                    NewCurrentBalance = table.Column<double>(type: "REAL", nullable: true),
                    NewDueDate = table.Column<string>(type: "TEXT", nullable: true),
                    OldDueDate = table.Column<string>(type: "TEXT", nullable: true),
                    OperatorInitials = table.Column<string>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPD_PawnPayments", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "XPD_Tickets",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    TransNo = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: true),
                    Active = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<double>(type: "REAL", nullable: true),
                    CurrentBalance = table.Column<double>(type: "REAL", nullable: true),
                    IssueDate = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<string>(type: "TEXT", nullable: true),
                    DateClosed = table.Column<string>(type: "TEXT", nullable: true),
                    HowClosed = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Item = table.Column<string>(type: "TEXT", nullable: true),
                    OperatorInitials = table.Column<string>(type: "TEXT", nullable: true),
                    GunTicket = table.Column<int>(type: "INTEGER", nullable: true),
                    LostTicket = table.Column<int>(type: "INTEGER", nullable: true),
                    PaidTillDate = table.Column<string>(type: "TEXT", nullable: true),
                    LastDate = table.Column<string>(type: "TEXT", nullable: true),
                    ChargesDue = table.Column<double>(type: "REAL", nullable: true),
                    StandardCharges = table.Column<double>(type: "REAL", nullable: true),
                    StandardPU = table.Column<double>(type: "REAL", nullable: true),
                    FullTermPU = table.Column<double>(type: "REAL", nullable: true),
                    FulltermRenew = table.Column<double>(type: "REAL", nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPD_Tickets", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XPD_CustomerPhones_PhoneNormalized",
                table: "XPD_CustomerPhones",
                column: "PhoneNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_XPD_Items_TicketKey",
                table: "XPD_Items",
                column: "TicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_XPD_PawnPayments_TicketKey",
                table: "XPD_PawnPayments",
                column: "TicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_XPD_Tickets_Active",
                table: "XPD_Tickets",
                column: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_XPD_Tickets_CustomerKey",
                table: "XPD_Tickets",
                column: "CustomerKey");

            migrationBuilder.CreateIndex(
                name: "IX_XPD_Tickets_DueDate",
                table: "XPD_Tickets",
                column: "DueDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XPD_CustomerPhones");

            migrationBuilder.DropTable(
                name: "XPD_Customers");

            migrationBuilder.DropTable(
                name: "XPD_Items");

            migrationBuilder.DropTable(
                name: "XPD_PawnPayments");

            migrationBuilder.DropTable(
                name: "XPD_Tickets");
        }
    }
}
