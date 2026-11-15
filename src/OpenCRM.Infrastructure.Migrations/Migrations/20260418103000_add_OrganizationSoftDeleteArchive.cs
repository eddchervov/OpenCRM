using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260418103000_add_OrganizationSoftDeleteArchive")]
    public partial class add_OrganizationSoftDeleteArchive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchiveDate",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionDate",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_IsArchived",
                table: "Organizations",
                column: "IsArchived");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_IsDeleted",
                table: "Organizations",
                column: "IsDeleted");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_IsArchived",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_IsDeleted",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ArchiveDate",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DeletionDate",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Organizations");
        }
    }
}
