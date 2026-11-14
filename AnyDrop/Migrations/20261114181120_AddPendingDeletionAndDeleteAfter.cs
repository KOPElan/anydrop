using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingDeletionAndDeleteAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteAfter",
                table: "ShareItems",
                type: "TEXT",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_ShareItems_DeleteAfter",
                table: "ShareItems",
                column: "DeleteAfter");

            migrationBuilder.CreateIndex(
                name: "IX_PendingFileDeletions_CreatedAt",
                table: "PendingFileDeletions",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingFileDeletions");

            migrationBuilder.DropIndex(
                name: "IX_ShareItems_DeleteAfter",
                table: "ShareItems");

            migrationBuilder.DropColumn(
                name: "DeleteAfter",
                table: "ShareItems");
        }
    }
}
