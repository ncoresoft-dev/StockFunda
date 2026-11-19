using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeqId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Isin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AttachmentFileSize = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HasXbrl = table.Column<bool>(type: "bit", nullable: false),
                    BroadcastDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisseminationTime = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Difference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_BroadcastDateTime",
                table: "StockAnnouncements",
                column: "BroadcastDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_SeqId",
                table: "StockAnnouncements",
                column: "SeqId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_Symbol",
                table: "StockAnnouncements",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_Symbol_BroadcastDateTime",
                table: "StockAnnouncements",
                columns: new[] { "Symbol", "BroadcastDateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAnnouncements");
        }
    }
}
