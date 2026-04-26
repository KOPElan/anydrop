using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezoneLanguageBurnDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BurnAfterReadingMinutes",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "SystemSettings",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "zh-CN");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "SystemSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "BurnAfterReadingMinutes", "Language", "TimeZoneId" },
                values: new object[] { 10, "zh-CN", "UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BurnAfterReadingMinutes",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "SystemSettings");
        }
    }
}
