using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThumbnailGenerationHour",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailPath",
                table: "ShareItems",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "ThumbnailGenerationHour",
                value: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailGenerationHour",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ThumbnailPath",
                table: "ShareItems");
        }
    }
}
