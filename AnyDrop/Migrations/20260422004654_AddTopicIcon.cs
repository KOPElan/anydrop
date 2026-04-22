using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Topics",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "chat_bubble");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Topics");
        }
    }
}
