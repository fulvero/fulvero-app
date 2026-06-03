using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LShopOzonWebReact.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultipleProductSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductSupplierLinks_CompanyId_OzonProductId",
                table: "ProductSupplierLinks");

            migrationBuilder.AddColumn<string>(
                name: "SupplierName",
                table: "ProductSupplierLinks",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "ProductSupplierLinks"
                SET "SupplierName" = 'Поставщик'
                WHERE "SupplierName" = ''
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplierLinks_CompanyId_OzonProductId",
                table: "ProductSupplierLinks",
                columns: new[] { "CompanyId", "OzonProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductSupplierLinks_CompanyId_OzonProductId",
                table: "ProductSupplierLinks");

            migrationBuilder.DropColumn(
                name: "SupplierName",
                table: "ProductSupplierLinks");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplierLinks_CompanyId_OzonProductId",
                table: "ProductSupplierLinks",
                columns: new[] { "CompanyId", "OzonProductId" },
                unique: true);
        }
    }
}
