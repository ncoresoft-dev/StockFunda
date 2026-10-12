using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMarketIndexConstituentFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "MarketIndexConstituents");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "MarketIndexConstituents");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CompanyMaster_Symbol",
                table: "CompanyMaster",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_MarketIndexConstituents_Symbol",
                table: "MarketIndexConstituents",
                column: "Symbol");

            migrationBuilder.AddForeignKey(
                name: "FK_MarketIndexConstituents_CompanyMaster_Symbol",
                table: "MarketIndexConstituents",
                column: "Symbol",
                principalTable: "CompanyMaster",
                principalColumn: "Symbol",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarketIndexConstituents_CompanyMaster_Symbol",
                table: "MarketIndexConstituents");

            migrationBuilder.DropIndex(
                name: "IX_MarketIndexConstituents_Symbol",
                table: "MarketIndexConstituents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CompanyMaster_Symbol",
                table: "CompanyMaster");

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "MarketIndexConstituents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "MarketIndexConstituents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
