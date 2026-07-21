using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingPurchasedEmber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingPurchasedEmbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    PurchasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPurchasedEmbers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingPurchasedEmbers_AccountId",
                table: "PendingPurchasedEmbers",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingPurchasedEmbers");
        }
    }
}
