using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveExtraColumnsFromMarketIndexConstituents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DividendYield",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "NetProfitTtm",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "PeRatio",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "ProfitGrowthYoy",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "RevenueGrowthQoq",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "RevenueTtm",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "Roce",
                table: "MarketIndexConstituents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DividendYield",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetProfitTtm",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PeRatio",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitGrowthYoy",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RevenueGrowthQoq",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RevenueTtm",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Roce",
                table: "MarketIndexConstituents",
                type: "decimal(18,2)",
                nullable: true);
        }
    }
}
