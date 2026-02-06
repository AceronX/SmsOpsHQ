using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Persistence;

// EF Core database context for the SmsOps HQ operational SQLite database.
// Phase 1 covers Stores and Users tables only.
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<StoreEntity> Stores => Set<StoreEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureStores(modelBuilder);
        ConfigureUsers(modelBuilder);
    }

    private static void ConfigureStores(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreEntity>(store =>
        {
            store.ToTable("Stores");
            store.HasKey(s => s.StoreId);

            store.Property(s => s.StoreName)
                .HasMaxLength(128)
                .IsRequired();

            store.Property(s => s.Address)
                .HasMaxLength(255);

            store.Property(s => s.City)
                .HasMaxLength(64);

            store.Property(s => s.State)
                .HasMaxLength(10);

            store.Property(s => s.Zip)
                .HasMaxLength(20);

            store.Property(s => s.Phone)
                .HasMaxLength(32);

            store.Property(s => s.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            store.Property(s => s.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // FK to TwilioNumbers (DefaultNumberId) is not modeled as a navigation
            // in Phase 1 because TwilioNumberEntity is out of scope. The column is
            // still stored; the FK constraint will be added with TwilioNumbers.
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.UserId);

            user.Property(u => u.FullName)
                .HasMaxLength(128)
                .IsRequired();

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

            // Indexes matching DATABASE_SCHEMA.md
            user.HasIndex(u => u.Username)
                .IsUnique();

            user.HasIndex(u => u.StoreId);

            // FK: Users.StoreId -> Stores.StoreId (nullable for HQ users)
            user.HasOne(u => u.Store)
                .WithMany(s => s.Users)
                .HasForeignKey(u => u.StoreId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });
    }
}
