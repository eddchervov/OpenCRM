using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

/// <summary>Добавляет отдельные поля акта приёмки автомобиля.</summary>
public partial class AddMaintenanceIntakeActFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "IntakeMileage",
            table: "Maintenance",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IntakeCompleteness",
            table: "Maintenance",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IntakeVisibleDamage",
            table: "Maintenance",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IntakeVisibleDamage", table: "Maintenance");
        migrationBuilder.DropColumn(name: "IntakeCompleteness", table: "Maintenance");
        migrationBuilder.DropColumn(name: "IntakeMileage", table: "Maintenance");
    }
}