using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProductSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OzonProductId = table.Column<long>(type: "bigint", nullable: false),
                    OfferId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSettings_CompanyId_OzonProductId",
                table: "ProductSettings",
                columns: new[] { "CompanyId", "OzonProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSettings");
        }
    }
}
