using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddClientIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyTypeCode",
                table: "OrganizationClients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "OrganizationClients",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "OrganizationClients",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "OrganizationClients",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "OrganizationClients" IN ACCESS EXCLUSIVE MODE;
                DO $client_identity$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationClients" WHERE "FirstName" IS NOT NULL OR "LastName" IS NOT NULL OR "MiddleName" IS NOT NULL OR "CompanyTypeCode" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Откат составных ФИО и вида организации невозможен без потери данных. Сначала сохраните резервную копию и выполните согласованное восстановление.';
                    END IF;
                END $client_identity$;
                """);

            migrationBuilder.DropColumn(
                name: "CompanyTypeCode",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "OrganizationClients");
        }
    }
}
