using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SetExistingClientPartyTypeToPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"OrganizationClients\" SET \"PartyTypeCode\" = 'person' WHERE \"PartyTypeCode\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "PartyTypeCode",
                table: "OrganizationClients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "person",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PartyTypeCode",
                table: "OrganizationClients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "person");
        }
    }
}
