using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAccountProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationTypeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSupported = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationTypeDefinitions", x => x.Id);
                    table.CheckConstraint("CK_OrganizationTypeDefinitions_SupportedCode", "\"IsSupported\" = (\"Code\" IN ('detailing', 'service-station', 'fleet'))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTypeDefinitions_Code",
                table: "OrganizationTypeDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTypeDefinitions_SortOrder",
                table: "OrganizationTypeDefinitions",
                column: "SortOrder");

            migrationBuilder.Sql("""
                INSERT INTO "OrganizationTypeDefinitions"
                    ("Id", "Code", "Name", "Description", "IsActive", "IsSupported", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT "Id", "Code", "Name", "Description", "IsActive",
                    "Code" IN ('detailing', 'service-station', 'fleet'), "SortOrder", "CreatedAt", "UpdatedAt"
                FROM "AccountScenarioDefinitions";

                SELECT setval(pg_get_serial_sequence('"OrganizationTypeDefinitions"', 'Id'),
                    COALESCE((SELECT MAX("Id") FROM "OrganizationTypeDefinitions"), 0) + 1, FALSE);

                INSERT INTO "OrganizationTypeDefinitions"
                    ("Code", "Name", "Description", "IsActive", "IsSupported", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT defaults.code, defaults.name, '', TRUE, TRUE, defaults.sort_order, NOW(), NOW()
                FROM (VALUES ('detailing', 'Детейлинг', 30),
                             ('service-station', 'СТО / автосервис', 20),
                             ('fleet', 'Автопарк', 50)) AS defaults(code, name, sort_order)
                WHERE NOT EXISTS (SELECT 1 FROM "OrganizationTypeDefinitions" WHERE "Code" = defaults.code);
                """);

            migrationBuilder.DropTable(
                name: "AccountScenarioFeatures");

            migrationBuilder.DropTable(
                name: "UserAccountFeatures");

            migrationBuilder.DropTable(
                name: "AccountFeatureDefinitions");

            migrationBuilder.DropTable(
                name: "AccountScenarioDefinitions");

            migrationBuilder.DropTable(
                name: "UserAccountProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountFeatureDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AudienceCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    FeatureTypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsAvailableNow = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountScenarioDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccountProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AccountMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrimaryScenarioCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccountProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAccountProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountScenarioFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountFeatureDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    AccountScenarioDefinitionId = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "UserAccountFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserAccountProfileId = table.Column<int>(type: "integer", nullable: false),
                    EnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccountFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAccountFeatures_UserAccountProfiles_UserAccountProfileId",
                        column: x => x.UserAccountProfileId,
                        principalTable: "UserAccountProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountFeatures_UserAccountProfileId_FeatureCode",
                table: "UserAccountFeatures",
                columns: new[] { "UserAccountProfileId", "FeatureCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountProfiles_UserId",
                table: "UserAccountProfiles",
                column: "UserId",
                unique: true);

            // Удалённые профили и произвольные сценарии восстанавливаются только из резервной копии.
            migrationBuilder.Sql("""
                INSERT INTO "AccountScenarioDefinitions"
                    ("Id", "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT "Id", "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt"
                FROM "OrganizationTypeDefinitions";
                SELECT setval(pg_get_serial_sequence('"AccountScenarioDefinitions"', 'Id'),
                    COALESCE((SELECT MAX("Id") FROM "AccountScenarioDefinitions"), 0) + 1, FALSE);
                """);

            migrationBuilder.DropTable(
                name: "OrganizationTypeDefinitions");
        }
    }
}
