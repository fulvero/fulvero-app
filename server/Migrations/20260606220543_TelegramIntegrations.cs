using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class TelegramIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: true),
                    ChatTitle = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramIntegrations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramNotificationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramNotificationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramNotificationStates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramIntegrations_CompanyId",
                table: "TelegramIntegrations",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramIntegrations_LinkCode",
                table: "TelegramIntegrations",
                column: "LinkCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotificationStates_CompanyId_NotificationKey",
                table: "TelegramNotificationStates",
                columns: new[] { "CompanyId", "NotificationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramIntegrations");

            migrationBuilder.DropTable(
                name: "TelegramNotificationStates");
        }
    }
}
