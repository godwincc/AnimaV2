using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDelveHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelveHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimaId = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    FloorIndexReached = table.Column<int>(type: "integer", nullable: false),
                    CombatsWon = table.Column<int>(type: "integer", nullable: false),
                    ElitesDefeated = table.Column<int>(type: "integer", nullable: false),
                    BossDefeated = table.Column<bool>(type: "boolean", nullable: false),
                    TeammateNamesJson = table.Column<string>(type: "text", nullable: false),
                    WispEarnedThisRun = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelveHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelveHistories_AccountId_AnimaId",
                table: "DelveHistories",
                columns: new[] { "AccountId", "AnimaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelveHistories");
        }
    }
}
