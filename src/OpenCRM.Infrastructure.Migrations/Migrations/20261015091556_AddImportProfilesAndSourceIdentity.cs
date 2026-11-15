using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddImportProfilesAndSourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "OrganizationImportDrafts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationImportIdentity",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationImportIdentity", x => new { x.OrganizationId, x.SourceKey, x.Kind, x.ExternalId });
                    table.CheckConstraint("CK_OrganizationImportIdentity_Target", "(\"Kind\" = 'client' AND \"ClientId\" IS NOT NULL AND \"VehicleId\" IS NULL AND \"OrderId\" IS NULL) OR (\"Kind\" = 'vehicle' AND \"ClientId\" IS NULL AND \"VehicleId\" IS NOT NULL AND \"OrderId\" IS NULL) OR (\"Kind\" = 'order' AND \"ClientId\" IS NULL AND \"VehicleId\" IS NULL AND \"OrderId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_OrganizationImportIdentity_Maintenance_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationImportIdentity_OrganizationClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationImportIdentity_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationImportIdentity_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationImportProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationImportProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationImportProfile_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportIdentity_ClientId",
                table: "OrganizationImportIdentity",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportIdentity_OrderId",
                table: "OrganizationImportIdentity",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportIdentity_VehicleId",
                table: "OrganizationImportIdentity",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationImportProfile_OrganizationId_UserId_Name",
                table: "OrganizationImportProfile",
                columns: new[] { "OrganizationId", "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationImportIdentity");

            migrationBuilder.DropTable(
                name: "OrganizationImportProfile");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "OrganizationImportDrafts");
        }
    }
}
