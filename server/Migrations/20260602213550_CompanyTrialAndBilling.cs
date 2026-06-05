using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompanyTrialAndBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastYooKassaPaymentId",
                table: "Companies",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndsAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW() + INTERVAL '3 days'");

            migrationBuilder.Sql("""UPDATE "Companies" SET "TrialEndsAt" = "CreatedAt" + INTERVAL '3 days';""");

            migrationBuilder.AddColumn<string>(
                name: "YooKassaPaymentMethodIdProtected",
                table: "Companies",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastYooKassaPaymentId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "YooKassaPaymentMethodIdProtected",
                table: "Companies");
        }
    }
}
