using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public sealed class AnyDropDbContext(DbContextOptions<AnyDropDbContext> options) : DbContext(options)
{
    public DbSet<ShareItem> ShareItems => Set<ShareItem>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<PendingFileDeletion> PendingFileDeletions => Set<PendingFileDeletion>();
    public DbSet<MissingFileReport> MissingFileReports => Set<MissingFileReport>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ChunkedUpload> ChunkedUploads => Set<ChunkedUpload>();
    public DbSet<ChunkedUploadChunk> ChunkedUploadChunks => Set<ChunkedUploadChunk>();

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
            entity.Property(e => e.ThumbnailPath).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.ExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.DeleteAfter)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            // S0-05（D5）：已标记延迟删除的消息对所有常规读路径不可见。
            // Stage 1 的清扫任务用 IgnoreQueryFilters() 才能看到它们。
            // S1-01：上传中的预占位行同样不可见——文件还没落盘确认，不能出现在列表里。
            entity.Property(e => e.UploadState).HasConversion<int>().HasDefaultValue(UploadState.Ready);
            entity.HasQueryFilter(e => e.DeleteAfter == null && e.UploadState == UploadState.Ready);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.DeleteAfter);
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

        modelBuilder.Entity<PendingFileDeletion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<MissingFileReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ResolvedBy).HasMaxLength(20);
            entity.Property(e => e.FirstDetectedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.LastSeenAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.ResolvedAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            // 同一消息同一路径同一时刻最多一条未解决记录；文件再次丢失可建新记录。
            entity.HasIndex(e => new { e.ShareItemId, e.StoragePath, e.Kind })
                .IsUnique()
                .HasFilter("IsResolved = 0");
            entity.HasIndex(e => new { e.IsResolved, e.LastSeenAt });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReplacedByTokenHash).HasMaxLength(64);
            entity.Property(e => e.ExpiresAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.RevokedAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.Property(e => e.LastUsedAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                    value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.FamilyId });
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<ChunkedUpload>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(260).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(127);
            entity.Property(e => e.TotalSha256).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.Property(e => e.ExpiresAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            // 同一用户内幂等键唯一：重复初始化返回同一会话，不建第二份。
            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<ChunkedUploadChunk>(entity =>
        {
            entity.HasKey(e => new { e.UploadId, e.ChunkIndex });
            entity.Property(e => e.ReceivedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.HasOne<ChunkedUpload>()
                .WithMany()
                .HasForeignKey(e => e.UploadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AutoFetchLinkPreview).HasDefaultValue(true);
            entity.Property(e => e.BurnAfterReadingMinutes).HasDefaultValue(10);
            entity.Property(e => e.Language).HasMaxLength(10).HasDefaultValue("zh-CN");
            entity.Property(e => e.AutoCleanupEnabled).HasDefaultValue(false);
            entity.Property(e => e.AutoCleanupMonths).HasDefaultValue(1);
            entity.Property(e => e.ThumbnailGenerationHour).HasDefaultValue(2);
            entity.Property(e => e.ScheduledThumbnailGenerationEnabled).HasDefaultValue(false);
            entity.Property(e => e.UpdatedAt)
                .HasConversion(
                    value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.HasData(new SystemSettings
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AutoFetchLinkPreview = true,
                BurnAfterReadingMinutes = 10,
                Language = "zh-CN",
                AutoCleanupEnabled = false,
                AutoCleanupMonths = 1,
                ThumbnailGenerationHour = 2,
                ScheduledThumbnailGenerationEnabled = false,
                UpdatedAt = DateTimeOffset.Parse("2026-04-19T00:00:00Z")
            });
        });
    }
}
