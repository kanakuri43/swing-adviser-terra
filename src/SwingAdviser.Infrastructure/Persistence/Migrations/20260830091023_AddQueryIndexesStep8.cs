using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexesStep8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_positions_status",
                table: "positions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_margin_lot_contract_term_revisions_final_repayment_date",
                table: "margin_lot_contract_term_revisions",
                column: "final_repayment_date",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_results_direction_score_instrument_id",
                table: "candidate_results",
                columns: new[] { "direction", "score", "instrument_id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_status",
                table: "ai_check_attempts",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_status",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_margin_lot_contract_term_revisions_final_repayment_date",
                table: "margin_lot_contract_term_revisions");

            migrationBuilder.DropIndex(
                name: "ix_candidate_results_direction_score_instrument_id",
                table: "candidate_results");

            migrationBuilder.DropIndex(
                name: "ix_ai_check_attempts_status",
                table: "ai_check_attempts");
        }
    }
}
