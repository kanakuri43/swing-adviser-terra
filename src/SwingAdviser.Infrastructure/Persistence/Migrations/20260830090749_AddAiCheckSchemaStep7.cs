using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCheckSchemaStep7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_check_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    candidate_result_id = table.Column<int>(type: "INTEGER", nullable: false),
                    triggering_daily_update_run_id = table.Column<int>(type: "INTEGER", nullable: true),
                    requested_by = table.Column<string>(type: "TEXT", nullable: false),
                    requested_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    normalized_input_snapshot_hash = table.Column<string>(type: "TEXT", nullable: false),
                    technical_input_manifest_id = table.Column<int>(type: "INTEGER", nullable: false),
                    strategy_snapshot_hash = table.Column<string>(type: "TEXT", nullable: false),
                    prompt_template_version = table.Column<string>(type: "TEXT", nullable: false),
                    prompt_template_hash = table.Column<string>(type: "TEXT", nullable: false),
                    cli_executable_path = table.Column<string>(type: "TEXT", nullable: false),
                    cli_version = table.Column<string>(type: "TEXT", nullable: true),
                    model = table.Column<string>(type: "TEXT", nullable: true),
                    timeout_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    sanitized_arguments = table.Column<string>(type: "TEXT", nullable: true),
                    exit_code = table.Column<int>(type: "INTEGER", nullable: true),
                    error_kind = table.Column<string>(type: "TEXT", nullable: true),
                    sanitized_stderr = table.Column<string>(type: "TEXT", nullable: true),
                    raw_response_hash = table.Column<string>(type: "TEXT", nullable: true),
                    structured_result_sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    is_stale = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_check_attempts", x => x.attempt_id);
                    table.ForeignKey(
                        name: "fk_ai_check_attempts_analysis_input_manifests_technical_input_manifest_id",
                        column: x => x.technical_input_manifest_id,
                        principalTable: "analysis_input_manifests",
                        principalColumn: "manifest_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_check_attempts_candidate_results_candidate_result_id",
                        column: x => x.candidate_result_id,
                        principalTable: "candidate_results",
                        principalColumn: "candidate_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_check_attempts_daily_update_runs_triggering_daily_update_run_id",
                        column: x => x.triggering_daily_update_run_id,
                        principalTable: "daily_update_runs",
                        principalColumn: "daily_update_run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_check_results",
                columns: table => new
                {
                    result_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    attempt_id = table.Column<int>(type: "INTEGER", nullable: false),
                    schema_version = table.Column<string>(type: "TEXT", nullable: false),
                    outcome = table.Column<string>(type: "TEXT", nullable: false),
                    verdict = table.Column<string>(type: "TEXT", nullable: true),
                    confidence = table.Column<string>(type: "TEXT", nullable: true),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    technical_view = table.Column<string>(type: "TEXT", nullable: true),
                    fundamental_view = table.Column<string>(type: "TEXT", nullable: true),
                    checked_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_check_results", x => x.result_id);
                    table.ForeignKey(
                        name: "fk_ai_check_results_ai_check_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "ai_check_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_check_evidence_items",
                columns: table => new
                {
                    evidence_item_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    result_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evidence_kind = table.Column<string>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_check_evidence_items", x => x.evidence_item_id);
                    table.ForeignKey(
                        name: "fk_ai_check_evidence_items_ai_check_results_result_id",
                        column: x => x.result_id,
                        principalTable: "ai_check_results",
                        principalColumn: "result_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_check_sources",
                columns: table => new
                {
                    source_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    result_id = table.Column<int>(type: "INTEGER", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    published_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    retrieved_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_check_sources", x => x.source_id);
                    table.ForeignKey(
                        name: "fk_ai_check_sources_ai_check_results_result_id",
                        column: x => x.result_id,
                        principalTable: "ai_check_results",
                        principalColumn: "result_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_check_evidence_citations",
                columns: table => new
                {
                    evidence_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    source_ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_check_evidence_citations", x => new { x.evidence_item_id, x.source_ordinal });
                    table.ForeignKey(
                        name: "fk_ai_check_evidence_citations_ai_check_evidence_items_evidence_item_id",
                        column: x => x.evidence_item_id,
                        principalTable: "ai_check_evidence_items",
                        principalColumn: "evidence_item_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_candidate_result_id",
                table: "ai_check_attempts",
                column: "candidate_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_technical_input_manifest_id",
                table: "ai_check_attempts",
                column: "technical_input_manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_triggering_daily_update_run_id",
                table: "ai_check_attempts",
                column: "triggering_daily_update_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_evidence_items_result_id_evidence_kind_ordinal",
                table: "ai_check_evidence_items",
                columns: new[] { "result_id", "evidence_kind", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_results_attempt_id",
                table: "ai_check_results",
                column: "attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_sources_result_id_ordinal",
                table: "ai_check_sources",
                columns: new[] { "result_id", "ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_check_evidence_citations");

            migrationBuilder.DropTable(
                name: "ai_check_sources");

            migrationBuilder.DropTable(
                name: "ai_check_evidence_items");

            migrationBuilder.DropTable(
                name: "ai_check_results");

            migrationBuilder.DropTable(
                name: "ai_check_attempts");
        }
    }
}
