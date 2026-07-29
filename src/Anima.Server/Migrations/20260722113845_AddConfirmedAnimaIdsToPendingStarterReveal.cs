using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anima.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedAnimaIdsToPendingStarterReveal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue is "[]" (a valid empty JSON array), NOT the EF-generated "" -- existing
            // rows all have NextUnnamedIndex==0 (nothing confirmed yet, verified against the real
            // dev DB before writing this migration), so an empty confirmed-list default is correct
            // for every pre-existing row, but it still needs to be valid JSON for
            // PendingStarterRevealRepository.LoadAsync's JsonSerializer.Deserialize call to succeed.
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedAnimaIdsJson",
                table: "PendingStarterReveals",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAnimaIdsJson",
                table: "PendingStarterReveals");
        }
    }
}
