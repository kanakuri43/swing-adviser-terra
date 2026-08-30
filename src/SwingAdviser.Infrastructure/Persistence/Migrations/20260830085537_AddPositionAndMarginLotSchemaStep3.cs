using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionAndMarginLotSchemaStep3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    position_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    side = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    applied_strategy_key = table.Column<string>(type: "TEXT", nullable: false),
                    applied_strategy_version = table.Column<string>(type: "TEXT", nullable: false),
                    memo = table.Column<string>(type: "TEXT", nullable: true),
                    source_candidate_result_id = table.Column<int>(type: "INTEGER", nullable: true),
                    opened_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    closed_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.position_id);
                    table.ForeignKey(
                        name: "fk_positions_candidate_results_source_candidate_result_id",
                        column: x => x.source_candidate_result_id,
                        principalTable: "candidate_results",
                        principalColumn: "candidate_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_executions",
                columns: table => new
                {
                    trade_execution_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    position_id = table.Column<int>(type: "INTEGER", nullable: false),
                    execution_role = table.Column<string>(type: "TEXT", nullable: false),
                    executed_at = table.Column<string>(type: "TEXT", nullable: false),
                    price = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    entered_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    prefilled_from_candidate_result_id = table.Column<int>(type: "INTEGER", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_executions", x => x.trade_execution_id);
                    table.ForeignKey(
                        name: "fk_trade_executions_candidate_results_prefilled_from_candidate_result_id",
                        column: x => x.prefilled_from_candidate_result_id,
                        principalTable: "candidate_results",
                        principalColumn: "candidate_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_executions_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_executions_trade_executions_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "trade_executions",
                        principalColumn: "trade_execution_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "margin_lots",
                columns: table => new
                {
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    position_id = table.Column<int>(type: "INTEGER", nullable: false),
                    opening_trade_execution_id = table.Column<int>(type: "INTEGER", nullable: false),
                    opened_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    current_quantity = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_margin_lots", x => x.margin_lot_id);
                    table.ForeignKey(
                        name: "fk_margin_lots_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_margin_lots_trade_executions_opening_trade_execution_id",
                        column: x => x.opening_trade_execution_id,
                        principalTable: "trade_executions",
                        principalColumn: "trade_execution_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "margin_lot_contract_term_revisions",
                columns: table => new
                {
                    contract_term_revision_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    margin_category = table.Column<string>(type: "TEXT", nullable: false),
                    broker = table.Column<string>(type: "TEXT", nullable: false),
                    product = table.Column<string>(type: "TEXT", nullable: false),
                    term_type = table.Column<string>(type: "TEXT", nullable: false),
                    final_repayment_date = table.Column<string>(type: "TEXT", nullable: true),
                    confirmed_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    evidence = table.Column<string>(type: "TEXT", nullable: true),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_revision_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_margin_lot_contract_term_revisions", x => x.contract_term_revision_id);
                    table.ForeignKey(
                        name: "fk_margin_lot_contract_term_revisions_margin_lot_contract_term_revisions_supersedes_revision_id",
                        column: x => x.supersedes_revision_id,
                        principalTable: "margin_lot_contract_term_revisions",
                        principalColumn: "contract_term_revision_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_margin_lot_contract_term_revisions_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_corporate_action_adjustments",
                columns: table => new
                {
                    adjustment_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    corporate_action_id = table.Column<int>(type: "INTEGER", nullable: false),
                    ratio = table.Column<string>(type: "TEXT", nullable: true),
                    quantity_before = table.Column<string>(type: "TEXT", nullable: true),
                    quantity_after = table.Column<string>(type: "TEXT", nullable: true),
                    cost_basis_before = table.Column<string>(type: "TEXT", nullable: true),
                    cost_basis_after = table.Column<string>(type: "TEXT", nullable: true),
                    atr_basis_before = table.Column<string>(type: "TEXT", nullable: true),
                    atr_basis_after = table.Column<string>(type: "TEXT", nullable: true),
                    stop_price_before = table.Column<string>(type: "TEXT", nullable: true),
                    stop_price_after = table.Column<string>(type: "TEXT", nullable: true),
                    take_profit_price_before = table.Column<string>(type: "TEXT", nullable: true),
                    take_profit_price_after = table.Column<string>(type: "TEXT", nullable: true),
                    reconciliation_status = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_corporate_action_adjustments", x => x.adjustment_id);
                    table.ForeignKey(
                        name: "fk_position_corporate_action_adjustments_corporate_actions_corporate_action_id",
                        column: x => x.corporate_action_id,
                        principalTable: "corporate_actions",
                        principalColumn: "corporate_action_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_corporate_action_adjustments_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_execution_lot_allocations",
                columns: table => new
                {
                    allocation_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    trade_execution_id = table.Column<int>(type: "INTEGER", nullable: false),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    quantity = table.Column<string>(type: "TEXT", nullable: false),
                    effective_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_execution_lot_allocations", x => x.allocation_id);
                    table.ForeignKey(
                        name: "fk_trade_execution_lot_allocations_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_execution_lot_allocations_trade_execution_lot_allocations_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "trade_execution_lot_allocations",
                        principalColumn: "allocation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_execution_lot_allocations_trade_executions_trade_execution_id",
                        column: x => x.trade_execution_id,
                        principalTable: "trade_executions",
                        principalColumn: "trade_execution_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_margin_lot_contract_term_revisions_margin_lot_id_revision",
                table: "margin_lot_contract_term_revisions",
                columns: new[] { "margin_lot_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_margin_lot_contract_term_revisions_supersedes_revision_id",
                table: "margin_lot_contract_term_revisions",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_margin_lots_opening_trade_execution_id",
                table: "margin_lots",
                column: "opening_trade_execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_margin_lots_position_id",
                table: "margin_lots",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_corporate_action_adjustments_corporate_action_id",
                table: "position_corporate_action_adjustments",
                column: "corporate_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_corporate_action_adjustments_margin_lot_id_corporate_action_id",
                table: "position_corporate_action_adjustments",
                columns: new[] { "margin_lot_id", "corporate_action_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_instrument_id",
                table: "positions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_source_candidate_result_id",
                table: "positions",
                column: "source_candidate_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_execution_lot_allocations_margin_lot_id",
                table: "trade_execution_lot_allocations",
                column: "margin_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_execution_lot_allocations_supersedes_id",
                table: "trade_execution_lot_allocations",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_execution_lot_allocations_trade_execution_id_margin_lot_id_revision",
                table: "trade_execution_lot_allocations",
                columns: new[] { "trade_execution_id", "margin_lot_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_executions_position_id",
                table: "trade_executions",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_executions_prefilled_from_candidate_result_id",
                table: "trade_executions",
                column: "prefilled_from_candidate_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_executions_supersedes_id",
                table: "trade_executions",
                column: "supersedes_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "margin_lot_contract_term_revisions");

            migrationBuilder.DropTable(
                name: "position_corporate_action_adjustments");

            migrationBuilder.DropTable(
                name: "trade_execution_lot_allocations");

            migrationBuilder.DropTable(
                name: "margin_lots");

            migrationBuilder.DropTable(
                name: "trade_executions");

            migrationBuilder.DropTable(
                name: "positions");
        }
    }
}
