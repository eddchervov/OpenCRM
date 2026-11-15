using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddAdminNavigationReadStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NavigationReadStates",
            schema: "admin",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                SectionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ViewedThroughUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NavigationReadStates", state => new { state.UserId, state.SectionCode });
                table.ForeignKey(
                    name: "FK_NavigationReadStates_AspNetUsers_UserId",
                    column: state => state.UserId,
                    principalSchema: "identity",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NavigationReadStates",
            schema: "admin");
    }
}
