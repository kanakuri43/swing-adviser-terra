using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCheckQueueInputAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ai_check_attempts_candidate_result_id",
                table: "ai_check_attempts");

            migrationBuilder.AddColumn<string>(
                name: "normalized_input_snapshot_json",
                table: "ai_check_attempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_candidate_result_id_normalized_input_snapshot_hash_model",
                table: "ai_check_attempts",
                columns: new[] { "candidate_result_id", "normalized_input_snapshot_hash", "model" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ai_check_attempts_candidate_result_id_normalized_input_snapshot_hash_model",
                table: "ai_check_attempts");

            migrationBuilder.DropColumn(
                name: "normalized_input_snapshot_json",
                table: "ai_check_attempts");

            migrationBuilder.CreateIndex(
                name: "ix_ai_check_attempts_candidate_result_id",
                table: "ai_check_attempts",
                column: "candidate_result_id");
        }
    }
}
