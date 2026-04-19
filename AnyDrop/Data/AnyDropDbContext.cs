using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public sealed class AnyDropDbContext(DbContextOptions<AnyDropDbContext> options) : DbContext(options)
{
    public DbSet<ShareItem> ShareItems => Set<ShareItem>();
    public DbSet<Topic> Topics => Set<Topic>();

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
    }
}
