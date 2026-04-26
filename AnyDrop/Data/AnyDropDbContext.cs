using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public sealed class AnyDropDbContext(DbContextOptions<AnyDropDbContext> options) : DbContext(options)
{
    public DbSet<ShareItem> ShareItems => Set<ShareItem>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Topic>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastMessagePreview).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.LastMessageAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.PinnedAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.ArchivedAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.Icon).HasMaxLength(100).HasDefaultValue("chat_bubble");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IsBuiltIn);
            entity.HasIndex(e => new { e.SortOrder, e.LastMessageAt });
            entity.HasIndex(e => new { e.IsPinned, e.PinnedAt, e.LastMessageAt });
            entity.HasIndex(e => new { e.IsArchived, e.ArchivedAt });
        });

        modelBuilder.Entity<ShareItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentType).HasConversion<int>();
            entity.Property(e => e.Content).HasMaxLength(10_000).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.MimeType).HasMaxLength(127);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.ExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => new { e.TopicId, e.CreatedAt });
            entity.HasOne<Topic>()
                .WithMany()
                .HasForeignKey(e => e.TopicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nickname).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.PasswordSalt).IsRequired();
            entity.Property(e => e.SessionVersion).HasDefaultValue(1);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.LastLoginAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.UpdatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

            // 单用户语义：固定唯一值索引，保证只有一条记录可插入
            entity.Property<int>("SingletonKey").HasDefaultValue(1);
            entity.HasIndex("SingletonKey").IsUnique();
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AutoFetchLinkPreview).HasDefaultValue(true);
            entity.Property(e => e.TimeZoneId).HasMaxLength(100).HasDefaultValue("UTC");
            entity.Property(e => e.BurnAfterReadingMinutes).HasDefaultValue(10);
            entity.Property(e => e.Language).HasMaxLength(10).HasDefaultValue("zh-CN");
            entity.Property(e => e.UpdatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.HasData(new SystemSettings
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AutoFetchLinkPreview = true,
                TimeZoneId = "UTC",
                BurnAfterReadingMinutes = 10,
                Language = "zh-CN",
                UpdatedAt = DateTimeOffset.Parse("2026-04-19T00:00:00Z")
            });
        });
    }
}
