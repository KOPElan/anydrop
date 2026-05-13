using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations;

[Migration("20260513141000_AddScheduledThumbnailGenerationFlag")]
public partial class AddScheduledThumbnailGenerationFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ScheduledThumbnailGenerationEnabled",
            table: "SystemSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ScheduledThumbnailGenerationEnabled",
            table: "SystemSettings");
    }
}
