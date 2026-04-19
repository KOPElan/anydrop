using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    public static async Task MigrateAndSeedAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();

        await db.Database.MigrateAsync(ct);

        if (!await db.SystemSettings.AnyAsync(ct))
        {
            db.SystemSettings.Add(new SystemSettings
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AutoFetchLinkPreview = true,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
