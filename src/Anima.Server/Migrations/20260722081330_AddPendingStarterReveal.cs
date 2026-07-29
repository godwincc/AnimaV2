using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingStarterReveal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingStarterReveals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RollsJson = table.Column<string>(type: "text", nullable: false),
                    NextUnnamedIndex = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingStarterReveals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingStarterReveals_AccountId",
                table: "PendingStarterReveals",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingStarterReveals");
        }
    }
}
