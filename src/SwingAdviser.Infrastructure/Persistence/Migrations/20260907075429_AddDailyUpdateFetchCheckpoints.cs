using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyUpdateFetchCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "daily_update_fetch_checkpoint_id",
                table: "external_fetch_results",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "daily_update_fetch_checkpoints",
                columns: table => new
                {
                    daily_update_fetch_checkpoint_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    daily_update_run_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    source_kind = table.Column<string>(type: "TEXT", nullable: false),
                    target_key = table.Column<string>(type: "TEXT", nullable: false),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: true),
                    requested_range_start_date = table.Column<string>(type: "TEXT", nullable: true),
                    covered_through_date = table.Column<string>(type: "TEXT", nullable: true),
                    data_revision_fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    invalidation_reason = table.Column<string>(type: "TEXT", nullable: true),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    valid_until_utc = table.Column<string>(type: "TEXT", nullable: false),
                    reused_from_checkpoint_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_update_fetch_checkpoints", x => x.daily_update_fetch_checkpoint_id);
                    table.ForeignKey(
                        name: "fk_daily_update_fetch_checkpoints_daily_update_fetch_checkpoints_reused_from_checkpoint_id",
                        column: x => x.reused_from_checkpoint_id,
                        principalTable: "daily_update_fetch_checkpoints",
                        principalColumn: "daily_update_fetch_checkpoint_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_update_fetch_checkpoints_daily_update_runs_daily_update_run_id",
                        column: x => x.daily_update_run_id,
                        principalTable: "daily_update_runs",
                        principalColumn: "daily_update_run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_update_fetch_checkpoints_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_fetch_results_daily_update_fetch_checkpoint_id",
                table: "external_fetch_results",
                column: "daily_update_fetch_checkpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_update_fetch_checkpoints_daily_update_run_id",
                table: "daily_update_fetch_checkpoints",
                column: "daily_update_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_update_fetch_checkpoints_instrument_id",
                table: "daily_update_fetch_checkpoints",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_update_fetch_checkpoints_resume_lookup",
                table: "daily_update_fetch_checkpoints",
                columns: new[] { "evaluation_bar_date", "source_kind", "target_key", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_update_fetch_checkpoints_reused_from_checkpoint_id",
                table: "daily_update_fetch_checkpoints",
                column: "reused_from_checkpoint_id");

            migrationBuilder.AddForeignKey(
                name: "fk_external_fetch_results_daily_update_fetch_checkpoints_daily_update_fetch_checkpoint_id",
                table: "external_fetch_results",
                column: "daily_update_fetch_checkpoint_id",
                principalTable: "daily_update_fetch_checkpoints",
                principalColumn: "daily_update_fetch_checkpoint_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_external_fetch_results_daily_update_fetch_checkpoints_daily_update_fetch_checkpoint_id",
                table: "external_fetch_results");

            migrationBuilder.DropTable(
                name: "daily_update_fetch_checkpoints");

            migrationBuilder.DropIndex(
                name: "ix_external_fetch_results_daily_update_fetch_checkpoint_id",
                table: "external_fetch_results");

            migrationBuilder.DropColumn(
                name: "daily_update_fetch_checkpoint_id",
                table: "external_fetch_results");
        }
    }
}
