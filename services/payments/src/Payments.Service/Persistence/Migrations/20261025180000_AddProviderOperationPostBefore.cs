using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Service.Persistence.Migrations;

public partial class AddProviderOperationPostBefore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PostBefore",
            schema: "payments",
            table: "ProviderOperations",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM payments.\"ProviderOperations\" WHERE \"PostBefore\" IS NOT NULL) THEN RAISE EXCEPTION 'Нельзя удалить срок исполнения принятых финансовых операций'; END IF; END; $$;");
        migrationBuilder.DropColumn(
            name: "PostBefore",
            schema: "payments",
            table: "ProviderOperations");
    }
}
