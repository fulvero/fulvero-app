using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompanyScopedOperationalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Supplies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ProductionTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ProductionFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Supplies"
                SET "CompanyId" = (SELECT "Id" FROM "Companies" ORDER BY "CreatedAt" LIMIT 1)
                WHERE "CompanyId" IS NULL;

                UPDATE "ProductionTasks"
                SET "CompanyId" = (SELECT "Id" FROM "Companies" ORDER BY "CreatedAt" LIMIT 1)
                WHERE "CompanyId" IS NULL;

                UPDATE "ProductionFiles"
                SET "CompanyId" = (SELECT "Id" FROM "Companies" ORDER BY "CreatedAt" LIMIT 1)
                WHERE "CompanyId" IS NULL;

                UPDATE "AuditLogs"
                SET "CompanyId" = (SELECT "Id" FROM "Companies" ORDER BY "CreatedAt" LIMIT 1)
                WHERE "CompanyId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "Supplies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "ProductionTasks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "ProductionFiles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Supplies_CompanyId",
                table: "Supplies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_CompanyId",
                table: "ProductionTasks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionFiles_CompanyId",
                table: "ProductionFiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId",
                table: "AuditLogs",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Companies_CompanyId",
                table: "AuditLogs",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionFiles_Companies_CompanyId",
                table: "ProductionFiles",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionTasks_Companies_CompanyId",
                table: "ProductionTasks",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Supplies_Companies_CompanyId",
                table: "Supplies",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Companies_CompanyId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionFiles_Companies_CompanyId",
                table: "ProductionFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionTasks_Companies_CompanyId",
                table: "ProductionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Supplies_Companies_CompanyId",
                table: "Supplies");

            migrationBuilder.DropIndex(
                name: "IX_Supplies_CompanyId",
                table: "Supplies");

            migrationBuilder.DropIndex(
                name: "IX_ProductionTasks_CompanyId",
                table: "ProductionTasks");

            migrationBuilder.DropIndex(
                name: "IX_ProductionFiles_CompanyId",
                table: "ProductionFiles");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CompanyId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Supplies");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ProductionTasks");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ProductionFiles");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AuditLogs");
        }
    }
}
