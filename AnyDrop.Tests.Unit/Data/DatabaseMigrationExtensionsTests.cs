using AnyDrop.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Data;

public class DatabaseMigrationExtensionsTests
{
    [Fact]
    public async Task MigrateWithCompatibilityAsync_LegacyDatabaseWithoutHistory_RebuildsMigrationHistory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "ShareItems" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ShareItems" PRIMARY KEY,
                    "ContentType" INTEGER NOT NULL,
                    "Content" TEXT NOT NULL,
                    "FileName" TEXT NULL,
                    "FileSize" INTEGER NULL,
                    "MimeType" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL
                );
                """;

            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AnyDropDbContext(options);

        await dbContext.MigrateWithCompatibilityAsync();

        using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = """
            SELECT COUNT(1)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = $migrationId;
            """;

        var parameter = historyCommand.CreateParameter();
        parameter.ParameterName = "$migrationId";
        parameter.Value = "20260418183210_InitialCreate";
        historyCommand.Parameters.Add(parameter);

        var appliedCount = Convert.ToInt32(await historyCommand.ExecuteScalarAsync());

        appliedCount.Should().Be(1);
    }
}
