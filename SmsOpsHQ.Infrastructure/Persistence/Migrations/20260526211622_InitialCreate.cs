using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsOpsHQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPhones",
                columns: table => new
                {
                    CustomerKey = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneNormalized = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PhoneType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PhoneOriginal = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MatchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsDirect = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPhones", x => new { x.CustomerKey, x.PhoneNormalized, x.PhoneType });
                });

            migrationBuilder.CreateTable(
                name: "Items",
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
                    Mfg = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<string>(type: "TEXT", nullable: true),
                    Karat = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PawnPayments",
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
                    table.PrimaryKey("PK_PawnPayments", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReviewAutomationState",
                columns: table => new
                {
                    StateId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMaxTicketKey = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewAutomationState", x => x.StateId);
                });

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

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DefaultNumberId = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
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
                    table.PrimaryKey("PK_Tickets", x => x.Key);
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
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    MiddleName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResPhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    BusPhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    EMailAddress = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DOB = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SSN = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IDNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IDIssueState = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    FirstTransaction = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastTransaction = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Warning = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SyncedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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
                name: "ReviewChannels",
                columns: table => new
                {
                    ReviewChannelId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReviewUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewChannels", x => x.ReviewChannelId);
                    table.ForeignKey(
                        name: "FK_ReviewChannels_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
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
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    TwilioNumberId = table.Column<int>(type: "INTEGER", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_Stores_StoreId",
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
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "General"),
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
                        name: "FK_Threads_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.SetNull);
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
                name: "ReviewRequests",
                columns: table => new
                {
                    ReviewRequestId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReviewChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageBody = table.Column<string>(type: "TEXT", nullable: false),
                    TwilioSid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Sent"),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRequests", x => x.ReviewRequestId);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_ReviewChannels_ReviewChannelId",
                        column: x => x.ReviewChannelId,
                        principalTable: "ReviewChannels",
                        principalColumn: "ReviewChannelId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "TemplateId",
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
                name: "IX_CustomerPhones_PhoneNormalized",
                table: "CustomerPhones",
                column: "PhoneNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerKey",
                table: "Customers",
                column: "CustomerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FirstName_LastName",
                table: "Customers",
                columns: new[] { "FirstName", "LastName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PhoneE164",
                table: "Customers",
                column: "PhoneE164");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ResPhone",
                table: "Customers",
                column: "ResPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId",
                table: "Customers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TicketKey",
                table: "Items",
                column: "TicketKey");

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
                name: "IX_PawnPayments_PaymentDate",
                table: "PawnPayments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_PawnPayments_TicketKey",
                table: "PawnPayments",
                column: "TicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedMessages_ReviewedByUserId",
                table: "QuarantinedMessages",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedMessages_StoreId",
                table: "QuarantinedMessages",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewChannels_StoreId",
                table: "ReviewChannels",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_CustomerId",
                table: "ReviewRequests",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_ReviewChannelId",
                table: "ReviewRequests",
                column: "ReviewChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_SentAt",
                table: "ReviewRequests",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_StoreId",
                table: "ReviewRequests",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_TemplateId",
                table: "ReviewRequests",
                column: "TemplateId");

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
                name: "IX_Threads_CustomerId",
                table: "Threads",
                column: "CustomerId");

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
                name: "IX_Tickets_Active",
                table: "Tickets",
                column: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CustomerKey",
                table: "Tickets",
                column: "CustomerKey");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DueDate",
                table: "Tickets",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Type",
                table: "Tickets",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_TwilioNumbers_PhoneE164",
                table: "TwilioNumbers",
                column: "PhoneE164",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TwilioNumbers_StoreId",
                table: "TwilioNumbers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_StoreId",
                table: "Users",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TwilioNumberId",
                table: "Users",
                column: "TwilioNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPhones");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "OptOuts");

            migrationBuilder.DropTable(
                name: "PawnPayments");

            migrationBuilder.DropTable(
                name: "QuarantinedMessages");

            migrationBuilder.DropTable(
                name: "ReviewAutomationState");

            migrationBuilder.DropTable(
                name: "ReviewRequests");

            migrationBuilder.DropTable(
                name: "SMS_Excluded");

            migrationBuilder.DropTable(
                name: "SMS_Reminders");

            migrationBuilder.DropTable(
                name: "SMS_Unsubscribed");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "TwilioNumbers");

            migrationBuilder.DropTable(
                name: "Threads");

            migrationBuilder.DropTable(
                name: "ReviewChannels");

            migrationBuilder.DropTable(
                name: "Templates");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
