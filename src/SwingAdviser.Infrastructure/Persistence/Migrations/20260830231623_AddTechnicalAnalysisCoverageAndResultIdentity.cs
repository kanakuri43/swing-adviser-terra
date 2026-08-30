using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalAnalysisCoverageAndResultIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_indicator_results_instrument_id_evaluation_bar_date_analyzed_at_utc_strategy_parameter_snapshot_id",
                table: "indicator_results");

            migrationBuilder.DropIndex(
                name: "ix_indicator_results_scan_run_id",
                table: "indicator_results");

            migrationBuilder.CreateTable(
                name: "daily_bar_history_coverages",
                columns: table => new
                {
                    daily_bar_history_coverage_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    earliest_returned_date = table.Column<string>(type: "TEXT", nullable: false),
                    latest_returned_date = table.Column<string>(type: "TEXT", nullable: false),
                    full_history_confirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    observed_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_bar_history_coverages", x => x.daily_bar_history_coverage_id);
                    table.ForeignKey(
                        name: "fk_daily_bar_history_coverages_daily_bar_history_coverages_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "daily_bar_history_coverages",
                        principalColumn: "daily_bar_history_coverage_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_bar_history_coverages_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_instrument_id",
                table: "indicator_results",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_scan_run_id_manifest_id_strategy_parameter_snapshot_id",
                table: "indicator_results",
                columns: new[] { "scan_run_id", "manifest_id", "strategy_parameter_snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_bar_history_coverages_instrument_id_source_revision",
                table: "daily_bar_history_coverages",
                columns: new[] { "instrument_id", "source", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_bar_history_coverages_supersedes_id",
                table: "daily_bar_history_coverages",
                column: "supersedes_id",
                unique: true,
                filter: "supersedes_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_bar_history_coverages");

            migrationBuilder.DropIndex(
                name: "ix_indicator_results_instrument_id",
                table: "indicator_results");

            migrationBuilder.DropIndex(
                name: "ix_indicator_results_scan_run_id_manifest_id_strategy_parameter_snapshot_id",
                table: "indicator_results");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_instrument_id_evaluation_bar_date_analyzed_at_utc_strategy_parameter_snapshot_id",
                table: "indicator_results",
                columns: new[] { "instrument_id", "evaluation_bar_date", "analyzed_at_utc", "strategy_parameter_snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_scan_run_id",
                table: "indicator_results",
                column: "scan_run_id");
        }
    }
}
