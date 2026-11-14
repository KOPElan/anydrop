using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkedUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChunkedUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 127, nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ChunkSize = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BurnAfterReading = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ShareItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkedUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChunkedUploadChunks",
                columns: table => new
                {
                    UploadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkedUploadChunks", x => new { x.UploadId, x.ChunkIndex });
                    table.ForeignKey(
                        name: "FK_ChunkedUploadChunks_ChunkedUploads_UploadId",
                        column: x => x.UploadId,
                        principalTable: "ChunkedUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkedUploads_ExpiresAt",
                table: "ChunkedUploads",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkedUploads_Status",
                table: "ChunkedUploads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkedUploads_UserId_IdempotencyKey",
                table: "ChunkedUploads",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkedUploadChunks");

            migrationBuilder.DropTable(
                name: "ChunkedUploads");
        }
    }
}
