using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationImportBatchStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "OrganizationImportDrafts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedRows",
                table: "OrganizationImportDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                table: "OrganizationImportDrafts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeState",
                table: "OrganizationImportDrafts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "OrganizationImportDrafts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ready");

            migrationBuilder.AddColumn<int>(
                name: "StorageVersion",
                table: "OrganizationImportDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TotalRows",
                table: "OrganizationImportDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrganizationImportFile",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationImportFile", x => x.DraftId);
                    table.ForeignKey(
                        name: "FK_OrganizationImportFile_OrganizationImportDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "OrganizationImportDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationImportRow",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ParentExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationImportRow", x => new { x.DraftId, x.Id });
                    table.ForeignKey(
                        name: "FK_OrganizationImportRow_OrganizationImportDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "OrganizationImportDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationImportSourceRow",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationImportSourceRow", x => new { x.DraftId, x.TableId, x.Number });
                    table.ForeignKey(
                        name: "FK_OrganizationImportSourceRow_OrganizationImportDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "OrganizationImportDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportDrafts_State",
                table: "OrganizationImportDrafts",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportRow_DraftId_Kind_ExternalId",
                table: "OrganizationImportRow",
                columns: new[] { "DraftId", "Kind", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportRow_DraftId_Kind_Status",
                table: "OrganizationImportRow",
                columns: new[] { "DraftId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportRow_DraftId_ParentExternalId",
                table: "OrganizationImportRow",
                columns: new[] { "DraftId", "ParentExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportRow_DraftId_Position",
                table: "OrganizationImportRow",
                columns: new[] { "DraftId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Построчные черновики нельзя восстановить в прежнем JSON-формате; рабочие записи сохраняются.
            migrationBuilder.Sql("DELETE FROM \"OrganizationImportDrafts\" WHERE \"StorageVersion\" = 2");

            migrationBuilder.DropTable(
                name: "OrganizationImportFile");

            migrationBuilder.DropTable(
                name: "OrganizationImportRow");

            migrationBuilder.DropTable(
                name: "OrganizationImportSourceRow");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationImportDrafts_State",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "ProcessedRows",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "ResumeState",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "State",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "StorageVersion",
                table: "OrganizationImportDrafts");

            migrationBuilder.DropColumn(
                name: "TotalRows",
                table: "OrganizationImportDrafts");
        }
    }
}
