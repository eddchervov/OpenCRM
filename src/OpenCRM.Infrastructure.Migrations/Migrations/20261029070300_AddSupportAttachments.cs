using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_SupportMessageEntries_Id_SupportMessageId",
                table: "SupportMessageEntries",
                columns: new[] { "Id", "SupportMessageId" });

            migrationBuilder.CreateTable(
                name: "SupportAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportMessageId = table.Column<int>(type: "integer", nullable: false),
                    EntryId = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAttachments", x => x.Id);
                    table.CheckConstraint("CK_SupportAttachments_SizeBytes", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportMessageEntries_EntryId_SupportMes~",
                        columns: x => new { x.EntryId, x.SupportMessageId },
                        principalTable: "SupportMessageEntries",
                        principalColumns: new[] { "Id", "SupportMessageId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportMessages_SupportMessageId",
                        column: x => x.SupportMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_EntryId_SupportMessageId",
                table: "SupportAttachments",
                columns: new[] { "EntryId", "SupportMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_SupportMessageId_EntryId_SortOrder",
                table: "SupportAttachments",
                columns: new[] { "SupportMessageId", "EntryId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // После загрузки файлов откат требует резерва метаданных; объекты хранилища остаются на месте.
            migrationBuilder.DropTable(
                name: "SupportAttachments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SupportMessageEntries_Id_SupportMessageId",
                table: "SupportMessageEntries");
        }
    }
}
