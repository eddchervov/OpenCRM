using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddVehiclePhotoAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehiclePhotos_User_Vehicle",
                table: "VehiclePhotos");

            migrationBuilder.AddColumn<Guid>(
                name: "AlbumId",
                table: "VehiclePhotos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "VehiclePhotos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "VehiclePhotos"
                SET "IsPrimary" = TRUE
                WHERE "IsDeleted" = FALSE
                  AND "Id" IN (
                      SELECT DISTINCT ON ("VehicleId") "Id"
                      FROM "VehiclePhotos"
                      WHERE "IsDeleted" = FALSE
                      ORDER BY "VehicleId", "CreatedAt"
                  );
                """);

            migrationBuilder.CreateTable(
                name: "VehiclePhotoAlbums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePhotoAlbums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehiclePhotoAlbums_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotos_AlbumId",
                table: "VehiclePhotos",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotos_User_Vehicle",
                table: "VehiclePhotos",
                columns: new[] { "UserId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotos_Vehicle_Album_Deleted",
                table: "VehiclePhotos",
                columns: new[] { "VehicleId", "AlbumId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotoAlbums_User_Vehicle_Deleted",
                table: "VehiclePhotoAlbums",
                columns: new[] { "UserId", "VehicleId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotoAlbums_VehicleId",
                table: "VehiclePhotoAlbums",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_VehiclePhotos_VehiclePhotoAlbums_AlbumId",
                table: "VehiclePhotos",
                column: "AlbumId",
                principalTable: "VehiclePhotoAlbums",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehiclePhotos_VehiclePhotoAlbums_AlbumId",
                table: "VehiclePhotos");

            migrationBuilder.DropTable(
                name: "VehiclePhotoAlbums");

            migrationBuilder.DropIndex(
                name: "IX_VehiclePhotos_AlbumId",
                table: "VehiclePhotos");

            migrationBuilder.DropIndex(
                name: "IX_VehiclePhotos_User_Vehicle",
                table: "VehiclePhotos");

            migrationBuilder.DropIndex(
                name: "IX_VehiclePhotos_Vehicle_Album_Deleted",
                table: "VehiclePhotos");

            migrationBuilder.DropColumn(
                name: "AlbumId",
                table: "VehiclePhotos");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "VehiclePhotos");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhotos_User_Vehicle",
                table: "VehiclePhotos",
                columns: new[] { "UserId", "VehicleId" },
                unique: true);
        }
    }
}
