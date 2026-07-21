using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingBossHatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingBossHatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenomeJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingBossHatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingBossHatches_AccountId",
                table: "PendingBossHatches",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingBossHatches");
        }
    }
}
