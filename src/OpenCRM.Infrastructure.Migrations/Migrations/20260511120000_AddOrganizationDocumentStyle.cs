using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Добавляет настройку оформления документов организации.
    /// </summary>
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260511120000_AddOrganizationDocumentStyle")]
    public partial class AddOrganizationDocumentStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentStyleCode",
                table: "Organizations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentStyleCode",
                table: "Organizations");
        }
    }
}
