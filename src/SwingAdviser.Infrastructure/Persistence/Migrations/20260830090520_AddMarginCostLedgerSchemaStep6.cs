using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarginCostLedgerSchemaStep6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "margin_cost_ledger_entries",
                columns: table => new
                {
                    ledger_entry_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    margin_lot_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cost_type = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    period_start = table.Column<string>(type: "TEXT", nullable: true),
                    period_end = table.Column<string>(type: "TEXT", nullable: true),
                    quantity = table.Column<string>(type: "TEXT", nullable: true),
                    amount = table.Column<string>(type: "TEXT", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    rate = table.Column<string>(type: "TEXT", nullable: true),
                    rate_unit = table.Column<string>(type: "TEXT", nullable: true),
                    day_count_convention = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    available_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    observed_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_margin_cost_ledger_entries", x => x.ledger_entry_id);
                    table.ForeignKey(
                        name: "fk_margin_cost_ledger_entries_margin_cost_ledger_entries_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "margin_cost_ledger_entries",
                        principalColumn: "ledger_entry_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_margin_cost_ledger_entries_margin_lots_margin_lot_id",
                        column: x => x.margin_lot_id,
                        principalTable: "margin_lots",
                        principalColumn: "margin_lot_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_margin_cost_ledger_entries_margin_lot_id_cost_type_period_start_period_end_revision",
                table: "margin_cost_ledger_entries",
                columns: new[] { "margin_lot_id", "cost_type", "period_start", "period_end", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_margin_cost_ledger_entries_supersedes_id",
                table: "margin_cost_ledger_entries",
                column: "supersedes_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "margin_cost_ledger_entries");
        }
    }
}
