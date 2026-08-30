using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketDataSchemaStep1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    first_observed_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.instrument_id);
                });

            migrationBuilder.CreateTable(
                name: "corporate_actions",
                columns: table => new
                {
                    corporate_action_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    action_type = table.Column<string>(type: "TEXT", nullable: false),
                    effective_date = table.Column<string>(type: "TEXT", nullable: false),
                    announced_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    available_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    first_observed_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    split_ratio_numerator = table.Column<int>(type: "INTEGER", nullable: true),
                    split_ratio_denominator = table.Column<int>(type: "INTEGER", nullable: true),
                    dividend_amount_per_share = table.Column<string>(type: "TEXT", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: true),
                    source_event_id = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_actions", x => x.corporate_action_id);
                    table.ForeignKey(
                        name: "fk_corporate_actions_corporate_actions_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "corporate_actions",
                        principalColumn: "corporate_action_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corporate_actions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_bars",
                columns: table => new
                {
                    daily_bar_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    trading_date = table.Column<string>(type: "TEXT", nullable: false),
                    open = table.Column<string>(type: "TEXT", nullable: false),
                    high = table.Column<string>(type: "TEXT", nullable: false),
                    low = table.Column<string>(type: "TEXT", nullable: false),
                    close = table.Column<string>(type: "TEXT", nullable: false),
                    volume = table.Column<long>(type: "INTEGER", nullable: false),
                    adj_close = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    fetched_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_bars", x => x.daily_bar_id);
                    table.ForeignKey(
                        name: "fk_daily_bars_daily_bars_supersedes_id",
                        column: x => x.supersedes_id,
                        principalTable: "daily_bars",
                        principalColumn: "daily_bar_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_bars_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fundamental_data_snapshots",
                columns: table => new
                {
                    fundamental_snapshot_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fetched_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    per = table.Column<string>(type: "TEXT", nullable: true),
                    pbr = table.Column<string>(type: "TEXT", nullable: true),
                    market_cap = table.Column<string>(type: "TEXT", nullable: true),
                    dividend_yield = table.Column<string>(type: "TEXT", nullable: true),
                    additional_metrics_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fundamental_data_snapshots", x => x.fundamental_snapshot_id);
                    table.ForeignKey(
                        name: "fk_fundamental_data_snapshots_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_master_revisions",
                columns: table => new
                {
                    instrument_master_revision_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    market_segment = table.Column<string>(type: "TEXT", nullable: false),
                    instrument_type = table.Column<string>(type: "TEXT", nullable: false),
                    listed_status = table.Column<string>(type: "TEXT", nullable: false),
                    scan_eligibility = table.Column<string>(type: "TEXT", nullable: false),
                    effective_at_date = table.Column<string>(type: "TEXT", nullable: false),
                    available_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    source_file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_revision_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_master_revisions", x => x.instrument_master_revision_id);
                    table.ForeignKey(
                        name: "fk_instrument_master_revisions_instrument_master_revisions_supersedes_revision_id",
                        column: x => x.supersedes_revision_id,
                        principalTable: "instrument_master_revisions",
                        principalColumn: "instrument_master_revision_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_instrument_master_revisions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "margin_regulation_revisions",
                columns: table => new
                {
                    margin_regulation_revision_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    instrument_id = table.Column<int>(type: "INTEGER", nullable: false),
                    system_margin_eligible = table.Column<string>(type: "TEXT", nullable: false),
                    general_margin_eligible = table.Column<string>(type: "TEXT", nullable: false),
                    short_sell_eligible = table.Column<string>(type: "TEXT", nullable: false),
                    regulation_flags_json = table.Column<string>(type: "TEXT", nullable: true),
                    effective_at_date = table.Column<string>(type: "TEXT", nullable: false),
                    available_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    supersedes_revision_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_margin_regulation_revisions", x => x.margin_regulation_revision_id);
                    table.ForeignKey(
                        name: "fk_margin_regulation_revisions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "instrument_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_margin_regulation_revisions_margin_regulation_revisions_supersedes_revision_id",
                        column: x => x.supersedes_revision_id,
                        principalTable: "margin_regulation_revisions",
                        principalColumn: "margin_regulation_revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_instrument_id_source_event_id_revision",
                table: "corporate_actions",
                columns: new[] { "instrument_id", "source_event_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_supersedes_id",
                table: "corporate_actions",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_instrument_id_trading_date_revision",
                table: "daily_bars",
                columns: new[] { "instrument_id", "trading_date", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_supersedes_id",
                table: "daily_bars",
                column: "supersedes_id");

            migrationBuilder.CreateIndex(
                name: "ix_fundamental_data_snapshots_instrument_id",
                table: "fundamental_data_snapshots",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_instrument_master_revisions_instrument_id_revision",
                table: "instrument_master_revisions",
                columns: new[] { "instrument_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instrument_master_revisions_supersedes_revision_id",
                table: "instrument_master_revisions",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_margin_regulation_revisions_instrument_id_revision",
                table: "margin_regulation_revisions",
                columns: new[] { "instrument_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_margin_regulation_revisions_supersedes_revision_id",
                table: "margin_regulation_revisions",
                column: "supersedes_revision_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_actions");

            migrationBuilder.DropTable(
                name: "daily_bars");

            migrationBuilder.DropTable(
                name: "fundamental_data_snapshots");

            migrationBuilder.DropTable(
                name: "instrument_master_revisions");

            migrationBuilder.DropTable(
                name: "margin_regulation_revisions");

            migrationBuilder.DropTable(
                name: "instruments");
        }
    }
}
