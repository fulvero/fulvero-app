using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupplyItemSupplierLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierName",
                table: "SupplyItems",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplierUrl",
                table: "SupplyItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierName",
                table: "SupplyItems");

            migrationBuilder.DropColumn(
                name: "SupplierUrl",
                table: "SupplyItems");
        }
    }
}
