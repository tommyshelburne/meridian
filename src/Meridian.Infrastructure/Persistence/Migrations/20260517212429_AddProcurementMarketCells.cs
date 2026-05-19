using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementMarketCells : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "procurement_market_cells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    naics_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    set_aside = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trailing_twelve_month_obligated = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_market_cells", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_procurement_market_cells_naics_code_state_set_aside",
                table: "procurement_market_cells",
                columns: new[] { "naics_code", "state", "set_aside" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "procurement_market_cells");
        }
    }
}
