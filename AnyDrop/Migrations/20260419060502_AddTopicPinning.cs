using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "Topics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAt",
                table: "Topics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_IsPinned_PinnedAt_LastMessageAt",
                table: "Topics",
                columns: new[] { "IsPinned", "PinnedAt", "LastMessageAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Topics_IsPinned_PinnedAt_LastMessageAt",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "Topics");
        }
    }
}
