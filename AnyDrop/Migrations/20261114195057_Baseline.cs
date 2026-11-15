using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingFileDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingFileDeletions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutoFetchLinkPreview = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    BurnAfterReadingMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    Language = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "zh-CN"),
                    AutoCleanupEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    AutoCleanupMonths = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    ThumbnailGenerationHour = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    ScheduledThumbnailGenerationEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastMessagePreview = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    PinnedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, defaultValue: "chat_bubble")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", nullable: false),
                    SessionVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SingletonKey = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShareItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 127, nullable: true),
                    LinkTitle = table.Column<string>(type: "TEXT", nullable: true),
                    LinkDescription = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeleteAfter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TopicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShareItems_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "AutoCleanupMonths", "AutoFetchLinkPreview", "BurnAfterReadingMinutes", "Language", "ThumbnailGenerationHour", "UpdatedAt" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), 1, true, 10, "zh-CN", 2, new DateTime(2026, 4, 19, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_PendingFileDeletions_CreatedAt",
                table: "PendingFileDeletions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShareItems_CreatedAt",
                table: "ShareItems",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShareItems_DeleteAfter",
                table: "ShareItems",
                column: "DeleteAfter");

            migrationBuilder.CreateIndex(
                name: "IX_ShareItems_ExpiresAt",
                table: "ShareItems",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShareItems_TopicId_CreatedAt",
                table: "ShareItems",
                columns: new[] { "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Topics_CreatedAt",
                table: "Topics",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_IsArchived_ArchivedAt",
                table: "Topics",
                columns: new[] { "IsArchived", "ArchivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Topics_IsBuiltIn",
                table: "Topics",
                column: "IsBuiltIn");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_IsPinned_PinnedAt_LastMessageAt",
                table: "Topics",
                columns: new[] { "IsPinned", "PinnedAt", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Topics_SortOrder_LastMessageAt",
                table: "Topics",
                columns: new[] { "SortOrder", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SingletonKey",
                table: "Users",
                column: "SingletonKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingFileDeletions");

            migrationBuilder.DropTable(
                name: "ShareItems");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Topics");
        }
    }
}
