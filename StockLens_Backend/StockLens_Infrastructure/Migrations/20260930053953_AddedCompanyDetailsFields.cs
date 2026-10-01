using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLens_Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedCompanyDetailsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "About",
                table: "CompanyMaster",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BseCode",
                table: "CompanyMaster",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EmployeesCount",
                table: "CompanyMaster",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyPointsJson",
                table: "CompanyMaster",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NseCode",
                table: "CompanyMaster",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "CompanyMaster",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "CompanyMaster",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "About",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "BseCode",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "EmployeesCount",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "KeyPointsJson",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "NseCode",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "CompanyMaster");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "CompanyMaster");
        }
    }
}
