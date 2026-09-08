using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanRunResultReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scan_run_result_uses",
                columns: table => new
                {
                    scan_run_result_use_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scan_run_id = table.Column<int>(type: "INTEGER", nullable: false),
                    indicator_result_id = table.Column<int>(type: "INTEGER", nullable: false),
                    use_kind = table.Column<string>(type: "TEXT", nullable: false),
                    used_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_run_result_uses", x => x.scan_run_result_use_id);
                    table.ForeignKey(
                        name: "fk_scan_run_result_uses_indicator_results_indicator_result_id",
                        column: x => x.indicator_result_id,
                        principalTable: "indicator_results",
                        principalColumn: "indicator_result_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_run_result_uses_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "scan_run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scan_run_result_uses_indicator_result_id",
                table: "scan_run_result_uses",
                column: "indicator_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_run_result_uses_scan_run_id_indicator_result_id",
                table: "scan_run_result_uses",
                columns: new[] { "scan_run_id", "indicator_result_id" },
                unique: true);

            // Prior versions stored an indicator only through its originating scan run.
            // Preserve that relationship as an explicit computed use before later scans can
            // reference the same immutable result.
            migrationBuilder.Sql("""
                INSERT INTO scan_run_result_uses
                    (scan_run_id, indicator_result_id, use_kind, used_at_utc)
                SELECT scan_run_id, indicator_result_id, 'Computed', created_at_utc
                FROM indicator_results;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scan_run_result_uses");
        }
    }
}
