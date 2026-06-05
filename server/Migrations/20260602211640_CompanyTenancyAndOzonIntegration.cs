using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompanyTenancyAndOzonIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    LoginName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OzonClientIdProtected = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OzonApiKeyProtected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SubscriptionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubscriptionPaidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            var defaultCompanyId = new Guid("11111111-1111-1111-1111-111111111111");
            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "Name", "LoginName", "OzonClientIdProtected", "OzonApiKeyProtected", "SubscriptionStatus", "SubscriptionPaidUntil", "CreatedAt" },
                values: new object[] { defaultCompanyId, "Default Company", "default company", string.Empty, string.Empty, "Trial", null, DateTimeOffset.UtcNow });

            migrationBuilder.Sql($"""UPDATE "Users" SET "CompanyId" = '{defaultCompanyId}' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId_UserName",
                table: "Users",
                columns: new[] { "CompanyId", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_LoginName",
                table: "Companies",
                column: "LoginName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId_UserName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }
    }
}
