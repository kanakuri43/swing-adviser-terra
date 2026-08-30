using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2DataIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_margin_regulation_revisions_supersedes_revision_id",
                table: "margin_regulation_revisions");

            migrationBuilder.DropIndex(
                name: "ix_instrument_master_revisions_supersedes_revision_id",
                table: "instrument_master_revisions");

            migrationBuilder.DropIndex(
                name: "ix_daily_bars_supersedes_id",
                table: "daily_bars");

            migrationBuilder.DropIndex(
                name: "ix_corporate_actions_supersedes_id",
                table: "corporate_actions");

            migrationBuilder.AlterColumn<string>(
                name: "announced_at_utc",
                table: "corporate_actions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "ix_margin_regulation_revisions_supersedes_revision_id",
                table: "margin_regulation_revisions",
                column: "supersedes_revision_id",
                unique: true,
                filter: "supersedes_revision_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_instrument_master_revisions_supersedes_revision_id",
                table: "instrument_master_revisions",
                column: "supersedes_revision_id",
                unique: true,
                filter: "supersedes_revision_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_supersedes_id",
                table: "daily_bars",
                column: "supersedes_id",
                unique: true,
                filter: "supersedes_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_supersedes_id",
                table: "corporate_actions",
                column: "supersedes_id",
                unique: true,
                filter: "supersedes_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_margin_regulation_revisions_supersedes_revision_id",
                table: "margin_regulation_revisions");

            migrationBuilder.DropIndex(
                name: "ix_instrument_master_revisions_supersedes_revision_id",
                table: "instrument_master_revisions");

            migrationBuilder.DropIndex(
                name: "ix_daily_bars_supersedes_id",
                table: "daily_bars");

            migrationBuilder.DropIndex(
                name: "ix_corporate_actions_supersedes_id",
                table: "corporate_actions");

            migrationBuilder.AlterColumn<string>(
                name: "announced_at_utc",
                table: "corporate_actions",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_margin_regulation_revisions_supersedes_revision_id",
                table: "margin_regulation_revisions",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_instrument_master_revisions_supersedes_revision_id",
                table: "instrument_master_revisions",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_supersedes_id",
                table: "daily_bars",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_supersedes_id",
                table: "corporate_actions",
                column: "supersedes_id");
        }
    }
}
