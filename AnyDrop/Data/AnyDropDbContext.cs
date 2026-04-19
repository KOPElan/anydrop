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
            entity.Property(e => e.SortOrder).IsRequired();
            entity.Property(e => e.IsBuiltIn).IsRequired();
            entity.Property(e => e.LastMessagePreview).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    value => value.ToString("O"),
                    value => DateTimeOffset.Parse(value));
            entity.Property(e => e.LastMessageAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.ToString("O") : null,
                    value => value == null ? null : DateTimeOffset.Parse(value));
            entity.HasIndex(e => new { e.SortOrder, e.LastMessageAt });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IsBuiltIn);
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
                    value => value.ToString("O"),
                    value => DateTimeOffset.Parse(value));
            entity.HasOne<Topic>()
                .WithMany()
                .HasForeignKey(e => e.TopicId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => new { e.TopicId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
