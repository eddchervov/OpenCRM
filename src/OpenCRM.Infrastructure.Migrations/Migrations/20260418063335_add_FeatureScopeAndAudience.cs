using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_FeatureScopeAndAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudienceCode",
                table: "FeatureDefinitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopeCode",
                table: "FeatureDefinitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AudienceCode",
                table: "AccountFeatureDefinitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeatureTypeCode",
                table: "AccountFeatureDefinitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudienceCode",
                table: "FeatureDefinitions");

            migrationBuilder.DropColumn(
                name: "ScopeCode",
                table: "FeatureDefinitions");

            migrationBuilder.DropColumn(
                name: "AudienceCode",
                table: "AccountFeatureDefinitions");

            migrationBuilder.DropColumn(
                name: "FeatureTypeCode",
                table: "AccountFeatureDefinitions");
        }
    }
}
