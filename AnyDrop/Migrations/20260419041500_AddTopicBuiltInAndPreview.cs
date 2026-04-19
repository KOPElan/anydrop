using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicBuiltInAndPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "Topics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastMessagePreview",
                table: "Topics",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_IsBuiltIn",
                table: "Topics",
                column: "IsBuiltIn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Topics_IsBuiltIn",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "LastMessagePreview",
                table: "Topics");
        }
    }
}
