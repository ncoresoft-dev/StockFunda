using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAnnualReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAnnualReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Year = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PdfUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAnnualReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAnnualReports_Stocks_StockId",
                        column: x => x.StockId,
                        principalTable: "Stocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnualReports_StockId_Year",
                table: "StockAnnualReports",
                columns: new[] { "StockId", "Year" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAnnualReports");
        }
    }
}
