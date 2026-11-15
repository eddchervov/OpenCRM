using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstUtmCampaign",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstUtmContent",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstUtmMedium",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstUtmSource",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstUtmTerm",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstVisitAt",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUtmCampaign",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUtmContent",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUtmMedium",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUtmSource",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUtmTerm",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVisitAt",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstUtmCampaign",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstUtmContent",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstUtmMedium",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstUtmSource",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstUtmTerm",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstVisitAt",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastUtmCampaign",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastUtmContent",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastUtmMedium",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastUtmSource",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastUtmTerm",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastVisitAt",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
