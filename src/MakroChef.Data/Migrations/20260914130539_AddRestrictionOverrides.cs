using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MakroChef.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestrictionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Restriction = table.Column<string>(type: "text", nullable: false),
                    SourceText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestrictionOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestrictionOverrides_UserId",
                table: "RestrictionOverrides",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestrictionOverrides");
        }
    }
}
