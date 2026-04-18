using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

/// <summary>
/// EF Core database context for AnyDrop.
/// </summary>
/// <param name="options">The configured context options.</param>
public sealed class AnyDropDbContext(DbContextOptions<AnyDropDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the share items table.
    /// </summary>
    public DbSet<ShareItem> ShareItems => Set<ShareItem>();

    /// <summary>
    /// Configures EF Core mappings for entities.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShareItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ContentType).HasConversion<int>();
            entity.Property(item => item.Content).HasMaxLength(ShareValidationRules.MaxTextLength).IsRequired();
            entity.Property(item => item.FileName).HasMaxLength(260);
            entity.Property(item => item.MimeType).HasMaxLength(127);
            entity.Property(item => item.CreatedAt)
                .HasConversion(
                    value => value.ToUnixTimeMilliseconds(),
                    value => DateTimeOffset.FromUnixTimeMilliseconds(value));
            entity.HasIndex(item => item.CreatedAt);
        });
    }
}
