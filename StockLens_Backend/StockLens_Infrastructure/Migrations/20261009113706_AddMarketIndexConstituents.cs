using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketIndexConstituents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketIndexConstituents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IndexName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sector = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PeRatio = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarketCap = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DividendYield = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetProfitTtm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ProfitGrowthYoy = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RevenueTtm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RevenueGrowthQoq = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Roce = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Exchange = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketIndexConstituents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketIndexConstituents_Index_Symbol",
                table: "MarketIndexConstituents",
                columns: new[] { "IndexName", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketIndexConstituents");
        }
    }
}
