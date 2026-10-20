using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualReportExtractedInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedInsightsJson",
                table: "StockAnnualReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "StockAnnualReports",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "StockAnnualReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "StockAnnualReports",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedInsightsJson",
                table: "StockAnnualReports");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "StockAnnualReports");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "StockAnnualReports");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "StockAnnualReports");
        }
    }
}
