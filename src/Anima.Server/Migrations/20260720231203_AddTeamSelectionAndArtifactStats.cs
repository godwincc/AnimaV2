using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamSelectionAndArtifactStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeamAnimaIdsJson",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArtifactStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactName = table.Column<string>(type: "text", nullable: false),
                    FirstDiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DelvesWonWithCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactStats_AccountId_ArtifactName",
                table: "ArtifactStats",
                columns: new[] { "AccountId", "ArtifactName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactStats");

            migrationBuilder.DropColumn(
                name: "TeamAnimaIdsJson",
                table: "Accounts");
        }
    }
}
