using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class LinkInvitationsAndWorkMaterials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "EmployeeId",
            table: "OrganizationInvitations",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "OperationId",
            table: "OrganizationMaterialMovements",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "EmployeeId",
            table: "OrganizationMaterialMovements",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "MemberId",
            table: "OrganizationMaterialMovements",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "CommandId",
            table: "OrganizationMaterialMovements",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrganizationInvitations_EmployeeId",
            table: "OrganizationInvitations",
            column: "EmployeeId");
        migrationBuilder.AddForeignKey(
            name: "FK_OrganizationInvitations_OrganizationEmployees_EmployeeId",
            table: "OrganizationInvitations",
            column: "EmployeeId",
            principalTable: "OrganizationEmployees",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.CreateIndex(
            name: "IX_OrganizationMaterialMovements_OperationId",
            table: "OrganizationMaterialMovements",
            column: "OperationId");
        migrationBuilder.CreateIndex(
            name: "IX_OrganizationMaterialMovements_CommandId",
            table: "OrganizationMaterialMovements",
            column: "CommandId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OrganizationInvitations_OrganizationEmployees_EmployeeId",
            table: "OrganizationInvitations");
        migrationBuilder.DropIndex(
            name: "IX_OrganizationInvitations_EmployeeId",
            table: "OrganizationInvitations");
        migrationBuilder.DropIndex(
            name: "IX_OrganizationMaterialMovements_OperationId",
            table: "OrganizationMaterialMovements");
        migrationBuilder.DropIndex(
            name: "IX_OrganizationMaterialMovements_CommandId",
            table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "EmployeeId", table: "OrganizationInvitations");
        migrationBuilder.DropColumn(name: "OperationId", table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "EmployeeId", table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "MemberId", table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "CommandId", table: "OrganizationMaterialMovements");
    }
}
