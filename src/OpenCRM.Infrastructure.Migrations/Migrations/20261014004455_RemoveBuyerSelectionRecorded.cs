using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuyerSelectionRecorded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBuyerSelectionRecorded",
                table: "Maintenance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Прежние значения флага утрачены; true сохраняет правило покупателя без подстановки владельца.
            migrationBuilder.AddColumn<bool>(
                name: "IsBuyerSelectionRecorded",
                table: "Maintenance",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
