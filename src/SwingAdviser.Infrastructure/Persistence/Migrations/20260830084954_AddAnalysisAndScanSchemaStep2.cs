using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisAndScanSchemaStep2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_input_manifests",
                columns: table => new
                {
                    manifest_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    analyzed_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    first_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    last_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    bar_count = table.Column<int>(type: "INTEGER", nullable: false),
                    price_revision_set_hash = table.Column<string>(type: "TEXT", nullable: false),
                    corporate_action_set_hash = table.Column<string>(type: "TEXT", nullable: false),
                    manifest_hash = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_input_manifests", x => x.manifest_id);
                    table.ForeignKey(
                        name: "fk_analysis_input_manifests_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_update_runs",
                columns: table => new
                {
                    daily_update_run_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    step_summary_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_update_runs", x => x.daily_update_run_id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_parameter_snapshots",
                columns: table => new
                {
                    strategy_parameter_snapshot_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    strategy_key = table.Column<string>(type: "TEXT", nullable: false),
                    strategy_version = table.Column<string>(type: "TEXT", nullable: false),
                    indicator_engine_version = table.Column<string>(type: "TEXT", nullable: false),
                    candidate_scoring_engine_version = table.Column<string>(type: "TEXT", nullable: false),
                    normalized_parameters_json = table.Column<string>(type: "TEXT", nullable: false),
                    content_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_parameter_snapshots", x => x.strategy_parameter_snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "analysis_input_manifest_bars",
                columns: table => new
                {
                    manifest_id = table.Column<int>(type: "INTEGER", nullable: false),
                    trading_date = table.Column<string>(type: "TEXT", nullable: false),
                    daily_bar_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_input_manifest_bars", x => new { x.manifest_id, x.trading_date });
                    table.ForeignKey(
                        name: "fk_analysis_input_manifest_bars_analysis_input_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "analysis_input_manifests",
                        principalColumn: "manifest_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_analysis_input_manifest_bars_daily_bars_daily_bar_id",
                        column: x => x.daily_bar_id,
                        principalTable: "daily_bars",
                        principalColumn: "daily_bar_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analysis_input_manifest_corporate_actions",
                columns: table => new
                {
                    manifest_id = table.Column<int>(type: "INTEGER", nullable: false),
                    corporate_action_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_input_manifest_corporate_actions", x => new { x.manifest_id, x.corporate_action_id });
                    table.ForeignKey(
                        name: "fk_analysis_input_manifest_corporate_actions_analysis_input_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "analysis_input_manifests",
                        principalColumn: "manifest_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_analysis_input_manifest_corporate_actions_corporate_actions_corporate_action_id",
                        column: x => x.corporate_action_id,
                        principalTable: "corporate_actions",
                        principalColumn: "corporate_action_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_fetch_results",
                columns: table => new
                {
                    fetch_result_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    daily_update_run_id = table.Column<int>(type: "INTEGER", nullable: true),
                    source_kind = table.Column<string>(type: "TEXT", nullable: false),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    error_kind = table.Column<string>(type: "TEXT", nullable: true),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    record_count = table.Column<int>(type: "INTEGER", nullable: true),
                    attempted_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_fetch_results", x => x.fetch_result_id);
                    table.ForeignKey(
                        name: "fk_external_fetch_results_daily_update_runs_daily_update_run_id",
                        column: x => x.daily_update_run_id,
                        principalTable: "daily_update_runs",
                        principalColumn: "daily_update_run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_external_fetch_results_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scan_runs",
                columns: table => new
                {
                    scan_run_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    daily_update_run_id = table.Column<int>(type: "INTEGER", nullable: true),
                    run_type = table.Column<string>(type: "TEXT", nullable: false),
                    universe_definition_hash = table.Column<string>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    total_instruments = table.Column<int>(type: "INTEGER", nullable: false),
                    succeeded_count = table.Column<int>(type: "INTEGER", nullable: false),
                    failed_count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_runs", x => x.scan_run_id);
                    table.ForeignKey(
                        name: "fk_scan_runs_daily_update_runs_daily_update_run_id",
                        column: x => x.daily_update_run_id,
                        principalTable: "daily_update_runs",
                        principalColumn: "daily_update_run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indicator_results",
                columns: table => new
                {
                    indicator_result_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scan_run_id = table.Column<int>(type: "INTEGER", nullable: false),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    analyzed_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    manifest_id = table.Column<int>(type: "INTEGER", nullable: false),
                    strategy_parameter_snapshot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    data_status = table.Column<string>(type: "TEXT", nullable: false),
                    history_available_count = table.Column<int>(type: "INTEGER", nullable: false),
                    history_required_count = table.Column<int>(type: "INTEGER", nullable: false),
                    macd_line = table.Column<string>(type: "TEXT", nullable: true),
                    macd_signal = table.Column<string>(type: "TEXT", nullable: true),
                    macd_histogram = table.Column<string>(type: "TEXT", nullable: true),
                    ema20 = table.Column<string>(type: "TEXT", nullable: true),
                    ema50 = table.Column<string>(type: "TEXT", nullable: true),
                    ema200 = table.Column<string>(type: "TEXT", nullable: true),
                    atr14 = table.Column<string>(type: "TEXT", nullable: true),
                    volume_ratio = table.Column<string>(type: "TEXT", nullable: true),
                    volume_reference_average = table.Column<string>(type: "TEXT", nullable: true),
                    volume_ratio_status = table.Column<string>(type: "TEXT", nullable: false),
                    raw_values_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_results", x => x.indicator_result_id);
                    table.ForeignKey(
                        name: "fk_indicator_results_analysis_input_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "analysis_input_manifests",
                        principalColumn: "manifest_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_indicator_results_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_indicator_results_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "scan_run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_indicator_results_strategy_parameter_snapshots_strategy_parameter_snapshot_id",
                        column: x => x.strategy_parameter_snapshot_id,
                        principalTable: "strategy_parameter_snapshots",
                        principalColumn: "strategy_parameter_snapshot_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scan_exclusions",
                columns: table => new
                {
                    scan_exclusion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scan_run_id = table.Column<int>(type: "INTEGER", nullable: false),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    history_available_count = table.Column<int>(type: "INTEGER", nullable: true),
                    history_required_count = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_exclusions", x => x.scan_exclusion_id);
                    table.ForeignKey(
                        name: "fk_scan_exclusions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_exclusions_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "scan_run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "candidate_results",
                columns: table => new
                {
                    candidate_result_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    indicator_result_id = table.Column<int>(type: "INTEGER", nullable: false),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    signal_purpose = table.Column<string>(type: "TEXT", nullable: false),
                    matched = table.Column<bool>(type: "INTEGER", nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: true),
                    confidence_label = table.Column<string>(type: "TEXT", nullable: true),
                    candidate_scoring_engine_version = table.Column<string>(type: "TEXT", nullable: false),
                    score_components_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_results", x => x.candidate_result_id);
                    table.ForeignKey(
                        name: "fk_candidate_results_indicator_results_indicator_result_id",
                        column: x => x.indicator_result_id,
                        principalTable: "indicator_results",
                        principalColumn: "indicator_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_results_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_input_manifest_bars_daily_bar_id",
                table: "analysis_input_manifest_bars",
                column: "daily_bar_id");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_input_manifest_corporate_actions_corporate_action_id",
                table: "analysis_input_manifest_corporate_actions",
                column: "corporate_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_input_manifests_instrument_id_evaluation_bar_date_analyzed_at_utc_manifest_hash",
                table: "analysis_input_manifests",
                columns: new[] { "instrument_id", "evaluation_bar_date", "analyzed_at_utc", "manifest_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidate_results_indicator_result_id_direction",
                table: "candidate_results",
                columns: new[] { "indicator_result_id", "direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidate_results_instrument_id",
                table: "candidate_results",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_fetch_results_daily_update_run_id",
                table: "external_fetch_results",
                column: "daily_update_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_fetch_results_instrument_id",
                table: "external_fetch_results",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_instrument_id_evaluation_bar_date_analyzed_at_utc_strategy_parameter_snapshot_id",
                table: "indicator_results",
                columns: new[] { "instrument_id", "evaluation_bar_date", "analyzed_at_utc", "strategy_parameter_snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_manifest_id",
                table: "indicator_results",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_scan_run_id",
                table: "indicator_results",
                column: "scan_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_results_strategy_parameter_snapshot_id",
                table: "indicator_results",
                column: "strategy_parameter_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_exclusions_instrument_id",
                table: "scan_exclusions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_exclusions_scan_run_id_instrument_id",
                table: "scan_exclusions",
                columns: new[] { "scan_run_id", "instrument_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scan_runs_daily_update_run_id",
                table: "scan_runs",
                column: "daily_update_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_strategy_parameter_snapshots_content_sha256",
                table: "strategy_parameter_snapshots",
                column: "content_sha256",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_input_manifest_bars");

            migrationBuilder.DropTable(
                name: "analysis_input_manifest_corporate_actions");

            migrationBuilder.DropTable(
                name: "candidate_results");

            migrationBuilder.DropTable(
                name: "external_fetch_results");

            migrationBuilder.DropTable(
                name: "scan_exclusions");

            migrationBuilder.DropTable(
                name: "indicator_results");

            migrationBuilder.DropTable(
                name: "analysis_input_manifests");

            migrationBuilder.DropTable(
                name: "scan_runs");

            migrationBuilder.DropTable(
                name: "strategy_parameter_snapshots");

            migrationBuilder.DropTable(
                name: "daily_update_runs");
        }
    }
}
