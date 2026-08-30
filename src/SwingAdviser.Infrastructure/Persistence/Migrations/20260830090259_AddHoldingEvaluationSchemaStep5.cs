using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingEvaluationSchemaStep5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lot_holding_evaluations",
                columns: table => new
                {
                    lot_evaluation_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    position_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    evaluated_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    daily_bar_id = table.Column<int>(type: "INTEGER", nullable: false),
                    risk_plan_revision_id = table.Column<int>(type: "INTEGER", nullable: false),
                    decision = table.Column<string>(type: "TEXT", nullable: true),
                    stop_reached_today = table.Column<bool>(type: "INTEGER", nullable: false),
                    target_reached_today = table.Column<bool>(type: "INTEGER", nullable: false),
                    prior_target_reach_state = table.Column<string>(type: "TEXT", nullable: false),
                    prior_target_first_reach_bar_date = table.Column<string>(type: "TEXT", nullable: true),
                    technical_reversal_state = table.Column<string>(type: "TEXT", nullable: false),
                    macd_reversal_state = table.Column<string>(type: "TEXT", nullable: false),
                    ema20reversal_state = table.Column<string>(type: "TEXT", nullable: false),
                    partial_exit_status = table.Column<string>(type: "TEXT", nullable: false),
                    partial_exit_candidate_quantity = table.Column<string>(type: "TEXT", nullable: true),
                    evaluation_outcome = table.Column<string>(type: "TEXT", nullable: false),
                    evaluation_evidence_json = table.Column<string>(type: "TEXT", nullable: false),
                    diagnostics_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lot_holding_evaluations", x => x.lot_evaluation_id);
                    table.ForeignKey(
                        name: "fk_lot_holding_evaluations_daily_bars_daily_bar_id",
                        column: x => x.daily_bar_id,
                        principalTable: "daily_bars",
                        principalColumn: "daily_bar_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_holding_evaluations_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_holding_evaluations_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_holding_evaluations_risk_plans_risk_plan_revision_id",
                        column: x => x.risk_plan_revision_id,
                        principalTable: "risk_plans",
                        principalColumn: "risk_plan_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_holding_evaluations",
                columns: table => new
                {
                    position_evaluation_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    position_id = table.Column<int>(type: "INTEGER", nullable: false),
                    evaluation_bar_date = table.Column<string>(type: "TEXT", nullable: false),
                    evaluated_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    aggregated_decision = table.Column<string>(type: "TEXT", nullable: true),
                    partial_exit_status = table.Column<string>(type: "TEXT", nullable: false),
                    partial_exit_total_candidate_quantity = table.Column<string>(type: "TEXT", nullable: true),
                    evaluation_outcome = table.Column<string>(type: "TEXT", nullable: false),
                    lot_evaluations_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_holding_evaluations", x => x.position_evaluation_id);
                    table.ForeignKey(
                        name: "fk_position_holding_evaluations_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lot_holding_evaluations_daily_bar_id",
                table: "lot_holding_evaluations",
                column: "daily_bar_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_holding_evaluations_margin_lot_id_evaluation_bar_date_evaluated_at_utc",
                table: "lot_holding_evaluations",
                columns: new[] { "margin_lot_id", "evaluation_bar_date", "evaluated_at_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lot_holding_evaluations_position_id",
                table: "lot_holding_evaluations",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_holding_evaluations_risk_plan_revision_id",
                table: "lot_holding_evaluations",
                column: "risk_plan_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_holding_evaluations_position_id_evaluation_bar_date_evaluated_at_utc",
                table: "position_holding_evaluations",
                columns: new[] { "position_id", "evaluation_bar_date", "evaluated_at_utc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lot_holding_evaluations");

            migrationBuilder.DropTable(
                name: "position_holding_evaluations");
        }
    }
}
