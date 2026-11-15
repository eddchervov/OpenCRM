using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_AccountScenariosAndFeaturesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccountFeatures_UserAccountProfileId_FeatureCode",
                table: "UserAccountFeatures");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryScenarioCode",
                table: "UserAccountProfiles",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "personal");

            migrationBuilder.AddColumn<string>(
                name: "FeatureCodeText",
                table: "UserAccountFeatures",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "UserAccountProfiles"
                SET "PrimaryScenarioCode" = CASE "PrimaryScenario"
                    WHEN 1 THEN 'personal'
                    WHEN 2 THEN 'service-station'
                    WHEN 3 THEN 'detailing'
                    WHEN 4 THEN 'wrapping'
                    WHEN 5 THEN 'fleet'
                    WHEN 7 THEN 'driving-school'
                    ELSE 'personal'
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "UserAccountFeatures"
                SET "FeatureCodeText" = CASE "FeatureCode"
                    WHEN 1 THEN 'personal-vehicles'
                    WHEN 2 THEN 'customer-vehicles'
                    WHEN 3 THEN 'company-fleet'
                    WHEN 4 THEN 'clients'
                    WHEN 5 THEN 'service-history'
                    WHEN 6 THEN 'expenses'
                    WHEN 7 THEN 'photos-documents'
                    WHEN 8 THEN 'before-after-photos'
                    WHEN 9 THEN 'reports'
                    WHEN 10 THEN 'maintenance-reminders'
                    WHEN 11 THEN 'scheduler'
                    WHEN 12 THEN 'team'
                    WHEN 13 THEN 'regulations'
                    WHEN 14 THEN 'moto-hours'
                    WHEN 15 THEN 'materials-parts'
                    WHEN 16 THEN 'branded-reports'
                    WHEN 17 THEN 'public-reports'
                    WHEN 18 THEN 'analytics'
                    WHEN 19 THEN 'training-vehicles'
                    WHEN 20 THEN 'students'
                    ELSE 'personal-vehicles'
                END;
                """);

            migrationBuilder.DropColumn(
                name: "PrimaryScenario",
                table: "UserAccountProfiles");

            migrationBuilder.DropColumn(
                name: "FeatureCode",
                table: "UserAccountFeatures");

            migrationBuilder.RenameColumn(
                name: "FeatureCodeText",
                table: "UserAccountFeatures",
                newName: "FeatureCode");

            migrationBuilder.CreateTable(
                name: "AccountFeatureDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    IsAvailableNow = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFeatureDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountScenarioDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountScenarioDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountScenarioFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountScenarioDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    AccountFeatureDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountScenarioFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountScenarioFeatures_AccountFeatureDefinitions_AccountFe~",
                        column: x => x.AccountFeatureDefinitionId,
                        principalTable: "AccountFeatureDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountScenarioFeatures_AccountScenarioDefinitions_AccountS~",
                        column: x => x.AccountScenarioDefinitionId,
                        principalTable: "AccountScenarioDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountFeatures_UserAccountProfileId_FeatureCode",
                table: "UserAccountFeatures",
                columns: new[] { "UserAccountProfileId", "FeatureCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountFeatureDefinitions_Code",
                table: "AccountFeatureDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountFeatureDefinitions_SortOrder",
                table: "AccountFeatureDefinitions",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_AccountScenarioDefinitions_Code",
                table: "AccountScenarioDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountScenarioDefinitions_SortOrder",
                table: "AccountScenarioDefinitions",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_AccountScenarioFeatures_AccountFeatureDefinitionId",
                table: "AccountScenarioFeatures",
                column: "AccountFeatureDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountScenarioFeatures_AccountScenarioDefinitionId_Account~",
                table: "AccountScenarioFeatures",
                columns: new[] { "AccountScenarioDefinitionId", "AccountFeatureDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountScenarioFeatures_SortOrder",
                table: "AccountScenarioFeatures",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountScenarioFeatures");

            migrationBuilder.DropTable(
                name: "AccountFeatureDefinitions");

            migrationBuilder.DropTable(
                name: "AccountScenarioDefinitions");

            migrationBuilder.DropColumn(
                name: "FeatureCode",
                table: "UserAccountFeatures");

            migrationBuilder.AddColumn<int>(
                name: "FeatureCode",
                table: "UserAccountFeatures",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryScenario",
                table: "UserAccountProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.DropColumn(
                name: "PrimaryScenarioCode",
                table: "UserAccountProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountFeatures_UserAccountProfileId_FeatureCode",
                table: "UserAccountFeatures",
                columns: new[] { "UserAccountProfileId", "FeatureCode" },
                unique: true);
        }
    }
}
