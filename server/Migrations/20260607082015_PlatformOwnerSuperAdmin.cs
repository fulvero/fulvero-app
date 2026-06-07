using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulvero.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlatformOwnerSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemCompany",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_IsSystemCompany",
                table: "Companies",
                column: "IsSystemCompany");

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    system_company_id uuid;
                BEGIN
                    SELECT "Id" INTO system_company_id
                    FROM "Companies"
                    WHERE "LoginName" = 'fulvero'
                    LIMIT 1;

                    IF system_company_id IS NULL THEN
                        system_company_id := '00000000-0000-0000-0000-000000000001';
                        INSERT INTO "Companies" (
                            "Id",
                            "Name",
                            "LoginName",
                            "OzonClientIdProtected",
                            "OzonApiKeyProtected",
                            "SubscriptionStatus",
                            "TrialEndsAt",
                            "SubscriptionPaidUntil",
                            "IsSystemCompany",
                            "LastYooKassaPaymentId",
                            "CreatedAt"
                        )
                        VALUES (
                            system_company_id,
                            'Fulvero',
                            'fulvero',
                            '',
                            '',
                            'Active',
                            now() + interval '100 years',
                            NULL,
                            TRUE,
                            '',
                            now()
                        );
                    ELSE
                        UPDATE "Companies"
                        SET
                            "Name" = 'Fulvero',
                            "IsSystemCompany" = TRUE,
                            "SubscriptionStatus" = 'Active',
                            "SubscriptionPaidUntil" = NULL
                        WHERE "Id" = system_company_id;
                    END IF;

                    UPDATE "Users"
                    SET
                        "CompanyId" = system_company_id,
                        "Role" = 'SuperAdmin',
                        "AllowedFeatures" = ''
                    WHERE lower("UserName") = 'genacrok';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_IsSystemCompany",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IsSystemCompany",
                table: "Companies");
        }
    }
}
