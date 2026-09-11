using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MakroChef.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tool = table.Column<string>(type: "text", nullable: false),
                    ArgsHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedAccessToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductNutritions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    ProteinPer100g = table.Column<decimal>(type: "numeric", nullable: true),
                    SugarPer100g = table.Column<decimal>(type: "numeric", nullable: true),
                    FatPer100g = table.Column<decimal>(type: "numeric", nullable: true),
                    KcalPer100g = table.Column<decimal>(type: "numeric", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductNutritions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOrderId = table.Column<string>(type: "text", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SilpoUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpCalls_CreatedAt",
                table: "McpCalls",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_McpTokens_UserId",
                table: "McpTokens",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductNutritions_ProductId",
                table: "ProductNutritions",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptSnapshots_UserId_SourceOrderId",
                table: "ReceiptSnapshots",
                columns: new[] { "UserId", "SourceOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SilpoUserId",
                table: "Users",
                column: "SilpoUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpCalls");

            migrationBuilder.DropTable(
                name: "McpTokens");

            migrationBuilder.DropTable(
                name: "ProductNutritions");

            migrationBuilder.DropTable(
                name: "ReceiptSnapshots");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
