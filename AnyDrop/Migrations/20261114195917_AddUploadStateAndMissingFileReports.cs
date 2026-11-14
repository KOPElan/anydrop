using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadStateAndMissingFileReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UploadState",
                table: "ShareItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "MissingFileReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingFileReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissingFileReports_IsResolved_LastSeenAt",
                table: "MissingFileReports",
                columns: new[] { "IsResolved", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MissingFileReports_ShareItemId_StoragePath_Kind",
                table: "MissingFileReports",
                columns: new[] { "ShareItemId", "StoragePath", "Kind" },
                unique: true,
                filter: "IsResolved = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissingFileReports");

            migrationBuilder.DropColumn(
                name: "UploadState",
                table: "ShareItems");
        }
    }
}
