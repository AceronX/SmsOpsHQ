using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Persistence;

// EF Core database context for the SmsOps HQ SQLite database.
// Tables: Stores, Users, TwilioNumbers; Customers, Tickets, Items, PawnPayments, CustomerPhones; Threads, Messages, Templates; OptOuts, QuarantinedMessages; SmsReminders, SmsExcluded, SmsUnsubscribed.
// See docs/XPD_SYNC_SCHEMA.md for XPD-synced vs app-built tables.
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Multi-tenant and auth
    public DbSet<StoreEntity> Stores => Set<StoreEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<TwilioNumberEntity> TwilioNumbers => Set<TwilioNumberEntity>();

    // XPD-synced pawn data (Customers, Tickets, Items, PawnPayments) + phone index
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<TicketEntity> Tickets => Set<TicketEntity>();
    public DbSet<ItemEntity> Items => Set<ItemEntity>();
    public DbSet<PawnPaymentEntity> PawnPayments => Set<PawnPaymentEntity>();
    public DbSet<CustomerPhoneEntity> CustomerPhones => Set<CustomerPhoneEntity>();

    // Messaging (inbox, threads, templates)
    public DbSet<ThreadEntity> Threads => Set<ThreadEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();

    // Compliance and ops
    public DbSet<OptOutEntity> OptOuts => Set<OptOutEntity>();
    public DbSet<QuarantinedMessageEntity> QuarantinedMessages => Set<QuarantinedMessageEntity>();

    // Reminder system
    public DbSet<SmsReminderEntity> SmsReminders => Set<SmsReminderEntity>();
    public DbSet<SmsExcludedEntity> SmsExcluded => Set<SmsExcludedEntity>();
    public DbSet<SmsUnsubscribedEntity> SmsUnsubscribed => Set<SmsUnsubscribedEntity>();

    // Review system
    public DbSet<ReviewChannelEntity> ReviewChannels => Set<ReviewChannelEntity>();
    public DbSet<ReviewRequestEntity> ReviewRequests => Set<ReviewRequestEntity>();
    public DbSet<ReviewAutomationStateEntity> ReviewAutomationState => Set<ReviewAutomationStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureStores(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureTwilioNumbers(modelBuilder);
        ConfigureCustomers(modelBuilder);
        ConfigureThreads(modelBuilder);
        ConfigureMessages(modelBuilder);
        ConfigureTemplates(modelBuilder);
        ConfigureOptOuts(modelBuilder);
        ConfigureQuarantinedMessages(modelBuilder);
        ConfigureTickets(modelBuilder);
        ConfigureItems(modelBuilder);
        ConfigurePawnPayments(modelBuilder);
        ConfigureCustomerPhones(modelBuilder);
        ConfigureSmsReminders(modelBuilder);
        ConfigureSmsExcluded(modelBuilder);
        ConfigureSmsUnsubscribed(modelBuilder);
        ConfigureReviewChannels(modelBuilder);
        ConfigureReviewRequests(modelBuilder);
        ConfigureReviewAutomationState(modelBuilder);
    }

    // ── Stores ────────────────────────────────────────────────────────

    private static void ConfigureStores(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreEntity>(store =>
        {
            store.ToTable("Stores");
            store.HasKey(s => s.StoreId);

            store.Property(s => s.StoreName)
                .HasMaxLength(128)
                .IsRequired();

            store.Property(s => s.Address).HasMaxLength(255);
            store.Property(s => s.City).HasMaxLength(64);
            store.Property(s => s.State).HasMaxLength(10);
            store.Property(s => s.Zip).HasMaxLength(20);

            store.Property(s => s.DefaultNumberId)
                .IsRequired()
                .HasDefaultValue(0);

            store.Property(s => s.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            store.Property(s => s.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // DefaultNumberId references TwilioNumbers.NumberId.
            // The FK constraint is not modeled in EF because SQLite cannot
            // add FK constraints to existing tables via ALTER TABLE.
            // Referential integrity is enforced at the application level.
        });
    }

    // ── Users ─────────────────────────────────────────────────────────

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.UserId);

            user.Property(u => u.Username)
                .HasMaxLength(64)
                .IsRequired();

            user.Property(u => u.PasswordHash)
                .HasMaxLength(255)
                .IsRequired();

            user.Property(u => u.Role)
                .HasMaxLength(32)
                .IsRequired();

            user.Property(u => u.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            user.Property(u => u.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            user.HasIndex(u => u.Username).IsUnique();
            user.HasIndex(u => u.StoreId);
            user.HasIndex(u => u.TwilioNumberId);

            user.HasOne(u => u.Store)
                .WithMany(s => s.Users)
                .HasForeignKey(u => u.StoreId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });
    }

    // ── TwilioNumbers ─────────────────────────────────────────────────

    private static void ConfigureTwilioNumbers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwilioNumberEntity>(tn =>
        {
            tn.ToTable("TwilioNumbers");
            tn.HasKey(t => t.NumberId);

            tn.Property(t => t.PhoneE164)
                .HasMaxLength(32)
                .IsRequired();

            tn.Property(t => t.FriendlyName).HasMaxLength(128);
            tn.Property(t => t.TwilioSid).HasMaxLength(64);
            tn.Property(t => t.MessagingServiceSid).HasMaxLength(64);

            tn.Property(t => t.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            tn.Property(t => t.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            tn.HasIndex(t => t.PhoneE164).IsUnique();

            tn.HasOne(t => t.Store)
                .WithMany(s => s.TwilioNumbers)
                .HasForeignKey(t => t.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── Customers ─────────────────────────────────────────────────────

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerEntity>(customer =>
        {
            customer.ToTable("Customers");
            customer.HasKey(c => c.CustomerId);

            customer.Property(c => c.PhoneE164)
                .HasMaxLength(32)
                .IsRequired();

            customer.Property(c => c.CellPhone).HasMaxLength(32);
            customer.Property(c => c.HomePhone).HasMaxLength(32);
            customer.Property(c => c.WorkPhone).HasMaxLength(32);
            customer.Property(c => c.FirstName).HasMaxLength(64);
            customer.Property(c => c.LastName).HasMaxLength(64);
            customer.Property(c => c.Address).HasMaxLength(255);
            customer.Property(c => c.City).HasMaxLength(64);
            customer.Property(c => c.State).HasMaxLength(10);
            customer.Property(c => c.Zip).HasMaxLength(20);

            customer.Property(c => c.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            customer.Property(c => c.UpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // XPawn-synced columns
            customer.Property(c => c.MiddleName).HasMaxLength(64);
            customer.Property(c => c.ResPhone).HasMaxLength(32);
            customer.Property(c => c.BusPhone).HasMaxLength(32);
            customer.Property(c => c.EMailAddress).HasMaxLength(128);
            customer.Property(c => c.DOB).HasMaxLength(32);
            customer.Property(c => c.SSN).HasMaxLength(32);
            customer.Property(c => c.IDNo).HasMaxLength(64);
            customer.Property(c => c.IDIssueState).HasMaxLength(10);
            customer.Property(c => c.FirstTransaction).HasMaxLength(32);
            customer.Property(c => c.LastTransaction).HasMaxLength(32);
            customer.Property(c => c.Warning).HasMaxLength(512);
            customer.Property(c => c.SyncedAt).HasMaxLength(64);

            customer.HasIndex(c => c.StoreId);
            customer.HasIndex(c => c.PhoneE164);
            customer.HasIndex(c => c.CustomerKey).IsUnique();
            customer.HasIndex(c => new { c.FirstName, c.LastName });
            customer.HasIndex(c => c.ResPhone);

            customer.HasOne(c => c.Store)
                .WithMany()
                .HasForeignKey(c => c.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── Threads ───────────────────────────────────────────────────────

    private static void ConfigureThreads(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThreadEntity>(thread =>
        {
            thread.ToTable("Threads");
            thread.HasKey(t => t.ThreadId);

            thread.Property(t => t.Status)
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue("Open");

            thread.Property(t => t.UnreadCount)
                .IsRequired()
                .HasDefaultValue(0);

            thread.Property(t => t.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // CustomerId and TwilioNumberId are deprecated columns kept
            // for backward compatibility. No FK constraints are modeled.

            thread.HasIndex(t => t.StoreId);
            thread.HasIndex(t => t.IdentityId);
            thread.HasIndex(t => t.LastMessageAt);
            thread.HasIndex(t => t.Status);

            thread.HasOne(t => t.Store)
                .WithMany()
                .HasForeignKey(t => t.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            // Optional FK for inbox customer lookup (no DB constraint; app-level only)
            thread.HasOne(t => t.Customer)
                .WithMany()
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            thread.HasOne(t => t.AssignedToUser)
                .WithMany()
                .HasForeignKey(t => t.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });
    }

    // ── Messages ──────────────────────────────────────────────────────

    private static void ConfigureMessages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageEntity>(message =>
        {
            message.ToTable("Messages");
            message.HasKey(m => m.MessageId);

            message.Property(m => m.StorePhone).HasMaxLength(32);

            message.Property(m => m.Direction)
                .HasMaxLength(16)
                .IsRequired();

            message.Property(m => m.FromE164)
                .HasMaxLength(32)
                .IsRequired();

            message.Property(m => m.ToE164)
                .HasMaxLength(32)
                .IsRequired();

            message.Property(m => m.Category)
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue("general");

            message.Property(m => m.Status)
                .HasMaxLength(32)
                .IsRequired();

            message.Property(m => m.TwilioSid).HasMaxLength(64);
            message.Property(m => m.ErrorCode).HasMaxLength(64);
            message.Property(m => m.ErrorText).HasMaxLength(512);

            message.Property(m => m.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // TwilioSid unique but nullable — notes/internal messages have no SID.
            // SQLite allows multiple NULLs in a unique index.
            message.HasIndex(m => m.TwilioSid).IsUnique();
            message.HasIndex(m => m.ThreadId);
            message.HasIndex(m => m.StoreId);
            message.HasIndex(m => m.CreatedAt);
            message.HasIndex(m => m.Category);

            message.HasOne(m => m.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(m => m.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            message.HasOne(m => m.Store)
                .WithMany()
                .HasForeignKey(m => m.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            message.HasOne(m => m.SentByUser)
                .WithMany()
                .HasForeignKey(m => m.SentByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });
    }

    // ── Templates ─────────────────────────────────────────────────────

    private static void ConfigureTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TemplateEntity>(template =>
        {
            template.ToTable("Templates");
            template.HasKey(t => t.TemplateId);

            template.Property(t => t.Name)
                .HasMaxLength(128)
                .IsRequired();

            template.Property(t => t.Body).IsRequired();
            template.Property(t => t.Hotkey).HasMaxLength(16);

            template.Property(t => t.Category)
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue("General");

            template.Property(t => t.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            template.Property(t => t.UpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            template.HasIndex(t => t.StoreId);

            template.HasOne(t => t.Store)
                .WithMany()
                .HasForeignKey(t => t.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            template.HasOne(t => t.CreatedByUser)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });
    }

    // ── OptOuts ───────────────────────────────────────────────────────

    private static void ConfigureOptOuts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OptOutEntity>(optOut =>
        {
            optOut.ToTable("OptOuts");
            optOut.HasKey(o => o.OptOutId);

            optOut.Property(o => o.PhoneE164)
                .HasMaxLength(32)
                .IsRequired();

            optOut.Property(o => o.OptOutDate)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            optOut.Property(o => o.Reason).HasMaxLength(255);

            // Unique constraint: one opt-out per phone per store
            optOut.HasIndex(o => new { o.StoreId, o.PhoneE164 }).IsUnique();

            optOut.HasOne(o => o.Store)
                .WithMany()
                .HasForeignKey(o => o.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── QuarantinedMessages ───────────────────────────────────────────

    private static void ConfigureQuarantinedMessages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuarantinedMessageEntity>(qm =>
        {
            qm.ToTable("QuarantinedMessages");
            qm.HasKey(q => q.QuarantineId);

            qm.Property(q => q.FromE164)
                .HasMaxLength(32)
                .IsRequired();

            qm.Property(q => q.ToE164)
                .HasMaxLength(32)
                .IsRequired();

            qm.Property(q => q.TwilioSid).HasMaxLength(64);
            qm.Property(q => q.QuarantineReason).HasMaxLength(255);
            qm.Property(q => q.Resolution).HasMaxLength(32);

            qm.Property(q => q.QuarantinedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            qm.HasIndex(q => q.StoreId);

            qm.HasOne(q => q.Store)
                .WithMany()
                .HasForeignKey(q => q.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            qm.HasOne(q => q.ReviewedByUser)
                .WithMany()
                .HasForeignKey(q => q.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });
    }

    // ── Tickets (XPD) ──────────────────────────────────────────────────

    private static void ConfigureTickets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketEntity>(xt =>
        {
            xt.ToTable("Tickets");
            xt.HasKey(t => t.Key);

            xt.HasIndex(t => t.CustomerKey);
            xt.HasIndex(t => t.Active);
            xt.HasIndex(t => t.Type);
            xt.HasIndex(t => t.DueDate);
        });
    }

    // ── Items (Pawn) ─────────────────────────────────────────────────

    private static void ConfigureItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemEntity>(xi =>
        {
            xi.ToTable("Items");
            xi.HasKey(i => i.Key);

            xi.HasIndex(i => i.TicketKey);
        });
    }

    // ── PawnPayments ──────────────────────────────────────────────────

    private static void ConfigurePawnPayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PawnPaymentEntity>(xp =>
        {
            xp.ToTable("PawnPayments");
            xp.HasKey(p => p.Key);
            xp.Property(p => p.Check).HasColumnName("Check_");
            xp.HasIndex(p => p.TicketKey);
            xp.HasIndex(p => p.PaymentDate);
        });
    }

    // ── CustomerPhones (phone index for fast lookup) ──────────────────

    private static void ConfigureCustomerPhones(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerPhoneEntity>(phone =>
        {
            phone.ToTable("CustomerPhones");
            phone.HasKey(p => new { p.CustomerKey, p.PhoneNormalized, p.SourceField });

            phone.Property(p => p.PhoneNormalized)
                .HasMaxLength(10)
                .IsRequired();

            phone.Property(p => p.PhoneOriginal).HasMaxLength(32);

            phone.Property(p => p.SourceField)
                .HasColumnName("PhoneType")
                .HasMaxLength(32)
                .IsRequired();

            phone.Property(p => p.MatchType).HasMaxLength(32);

            phone.HasIndex(p => p.PhoneNormalized);
        });
    }

    // ── SMS_Reminders ─────────────────────────────────────────────────

    private static void ConfigureSmsReminders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmsReminderEntity>(reminder =>
        {
            reminder.ToTable("SMS_Reminders");
            reminder.HasKey(r => r.Id);

            reminder.Property(r => r.Phone).HasMaxLength(32);
            reminder.Property(r => r.ReminderType).HasMaxLength(64);
            reminder.Property(r => r.TwilioSid).HasMaxLength(64);
            reminder.Property(r => r.ErrorMessage).HasMaxLength(512);
            reminder.Property(r => r.StorePhone).HasMaxLength(32);

            reminder.Property(r => r.Status)
                .IsRequired()
                .HasDefaultValue(0);

            reminder.Property(r => r.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("datetime('now')");

            reminder.HasIndex(r => r.TicketKey);
            reminder.HasIndex(r => r.CustomerKey);
            reminder.HasIndex(r => r.CreatedAt);
        });
    }

    // ── SMS_Excluded ──────────────────────────────────────────────────

    private static void ConfigureSmsExcluded(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmsExcludedEntity>(excluded =>
        {
            excluded.ToTable("SMS_Excluded");
            excluded.HasKey(e => e.Id);

            excluded.Property(e => e.Phone)
                .HasMaxLength(32)
                .IsRequired();

            excluded.Property(e => e.Reason).HasMaxLength(255);

            excluded.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("datetime('now')");

            excluded.HasIndex(e => e.Phone);
        });
    }

    // ── SMS_Unsubscribed ──────────────────────────────────────────────

    private static void ConfigureSmsUnsubscribed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmsUnsubscribedEntity>(unsub =>
        {
            unsub.ToTable("SMS_Unsubscribed");
            unsub.HasKey(u => u.Id);

            unsub.Property(u => u.Phone)
                .HasMaxLength(32)
                .IsRequired();

            unsub.Property(u => u.Method).HasMaxLength(32);
            unsub.Property(u => u.Notes).HasMaxLength(512);

            unsub.Property(u => u.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("datetime('now')");

            unsub.HasIndex(u => u.Phone);
        });
    }

    // ── ReviewChannels ────────────────────────────────────────────────

    private static void ConfigureReviewChannels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewChannelEntity>(rc =>
        {
            rc.ToTable("ReviewChannels");
            rc.HasKey(r => r.ReviewChannelId);

            rc.Property(r => r.PlatformName)
                .HasMaxLength(64)
                .IsRequired();

            rc.Property(r => r.ReviewUrl)
                .HasMaxLength(512)
                .IsRequired();

            rc.Property(r => r.SortOrder)
                .IsRequired()
                .HasDefaultValue(0);

            rc.Property(r => r.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            rc.Property(r => r.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            rc.HasIndex(r => r.StoreId);

            rc.HasOne(r => r.Store)
                .WithMany()
                .HasForeignKey(r => r.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── ReviewRequests ────────────────────────────────────────────────

    private static void ConfigureReviewRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewRequestEntity>(rr =>
        {
            rr.ToTable("ReviewRequests");
            rr.HasKey(r => r.ReviewRequestId);

            rr.Property(r => r.PhoneE164)
                .HasMaxLength(32)
                .IsRequired();

            rr.Property(r => r.MessageBody).IsRequired();

            rr.Property(r => r.TwilioSid).HasMaxLength(64);

            rr.Property(r => r.Status)
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue("Sent");

            rr.Property(r => r.SentAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            rr.HasIndex(r => r.StoreId);
            rr.HasIndex(r => r.CustomerId);
            rr.HasIndex(r => r.SentAt);

            rr.HasOne(r => r.Store)
                .WithMany()
                .HasForeignKey(r => r.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            rr.HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            rr.HasOne(r => r.ReviewChannel)
                .WithMany()
                .HasForeignKey(r => r.ReviewChannelId)
                .OnDelete(DeleteBehavior.Restrict);

            rr.HasOne(r => r.Template)
                .WithMany()
                .HasForeignKey(r => r.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── ReviewAutomationState (single row) ───────────────────────────

    private static void ConfigureReviewAutomationState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewAutomationStateEntity>(s =>
        {
            s.ToTable("ReviewAutomationState");
            s.HasKey(x => x.StateId);
            s.Property(x => x.StateId).ValueGeneratedNever();
        });
    }
}
