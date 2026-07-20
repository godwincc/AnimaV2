using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingWeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingWeaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentAId = table.Column<string>(type: "text", nullable: false),
                    ParentBId = table.Column<string>(type: "text", nullable: false),
                    WispCost = table.Column<int>(type: "integer", nullable: false),
                    PrimaryJson = table.Column<string>(type: "text", nullable: false),
                    TwinJson = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingWeaves", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingWeaves_AccountId",
                table: "PendingWeaves",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingWeaves");
        }
    }
}
