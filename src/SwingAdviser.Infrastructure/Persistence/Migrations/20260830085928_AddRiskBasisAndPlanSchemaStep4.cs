using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskBasisAndPlanSchemaStep4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_basis_snapshots",
                columns: table => new
                {
                    risk_basis_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    entry_basis_price = table.Column<string>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    atr_basis = table.Column<string>(type: "TEXT", nullable: false),
                    atr_reference_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    atr_period = table.Column<int>(type: "INTEGER", nullable: false),
                    atr_algorithm_version = table.Column<string>(type: "TEXT", nullable: false),
                    price_unit_basis_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    source_candidate_result_id = table.Column<int>(type: "INTEGER", nullable: true),
                    source_indicator_result_id = table.Column<int>(type: "INTEGER", nullable: true),
                    manual_open_analysis_input_manifest_id = table.Column<int>(type: "INTEGER", nullable: true),
                    strategy_parameter_snapshot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    corporate_action_set_hash = table.Column<string>(type: "TEXT", nullable: false),
                    content_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_basis_snapshots", x => x.risk_basis_id);
                    table.ForeignKey(
                        name: "fk_risk_basis_snapshots_analysis_input_manifests_manual_open_analysis_input_manifest_id",
                        column: x => x.manual_open_analysis_input_manifest_id,
                        principalTable: "analysis_input_manifests",
                        principalColumn: "manifest_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_basis_snapshots_candidate_results_source_candidate_result_id",
                        column: x => x.source_candidate_result_id,
                        principalTable: "candidate_results",
                        principalColumn: "candidate_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_basis_snapshots_indicator_results_source_indicator_result_id",
                        column: x => x.source_indicator_result_id,
                        principalTable: "indicator_results",
                        principalColumn: "indicator_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_basis_snapshots_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_basis_snapshots_strategy_parameter_snapshots_strategy_parameter_snapshot_id",
                        column: x => x.strategy_parameter_snapshot_id,
                        principalTable: "strategy_parameter_snapshots",
                        principalColumn: "strategy_parameter_snapshot_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_plans",
                columns: table => new
                {
                    risk_plan_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    plan_kind = table.Column<string>(type: "TEXT", nullable: false),
                    risk_basis_id = table.Column<int>(type: "INTEGER", nullable: false),
                    stop_price = table.Column<string>(type: "TEXT", nullable: false),
                    take_profit_price = table.Column<string>(type: "TEXT", nullable: false),
                    partial_take_profit_fraction = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_trade_execution_id = table.Column<int>(type: "INTEGER", nullable: true),
                    trigger_allocation_id = table.Column<int>(type: "INTEGER", nullable: true),
                    effective_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    supersedes_revision_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_plans", x => x.risk_plan_id);
                    table.ForeignKey(
                        name: "fk_risk_plans_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_plans_risk_basis_snapshots_risk_basis_id",
                        column: x => x.risk_basis_id,
                        principalTable: "risk_basis_snapshots",
                        principalColumn: "risk_basis_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_plans_risk_plans_supersedes_revision_id",
                        column: x => x.supersedes_revision_id,
                        principalTable: "risk_plans",
                        principalColumn: "risk_plan_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_plans_trade_execution_lot_allocations_trigger_allocation_id",
                        column: x => x.trigger_allocation_id,
                        principalTable: "trade_execution_lot_allocations",
                        principalColumn: "allocation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_plans_trade_executions_trigger_trade_execution_id",
                        column: x => x.trigger_trade_execution_id,
                        principalTable: "trade_executions",
                        principalColumn: "trade_execution_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_risk_basis_snapshots_manual_open_analysis_input_manifest_id",
                table: "risk_basis_snapshots",
                column: "manual_open_analysis_input_manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_basis_snapshots_margin_lot_id",
                table: "risk_basis_snapshots",
                column: "margin_lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_risk_basis_snapshots_source_candidate_result_id",
                table: "risk_basis_snapshots",
                column: "source_candidate_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_basis_snapshots_source_indicator_result_id",
                table: "risk_basis_snapshots",
                column: "source_indicator_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_basis_snapshots_strategy_parameter_snapshot_id",
                table: "risk_basis_snapshots",
                column: "strategy_parameter_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_plans_margin_lot_id_revision",
                table: "risk_plans",
                columns: new[] { "margin_lot_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_risk_plans_risk_basis_id",
                table: "risk_plans",
                column: "risk_basis_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_plans_supersedes_revision_id",
                table: "risk_plans",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_plans_trigger_allocation_id",
                table: "risk_plans",
                column: "trigger_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_plans_trigger_trade_execution_id",
                table: "risk_plans",
                column: "trigger_trade_execution_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_plans");

            migrationBuilder.DropTable(
                name: "risk_basis_snapshots");
        }
    }
}
