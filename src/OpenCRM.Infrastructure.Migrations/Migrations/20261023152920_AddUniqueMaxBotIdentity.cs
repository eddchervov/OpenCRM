using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMaxBotIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "OrganizationMaxBots"
                        WHERE "ImmutableProviderBotId" IS NOT NULL
                        GROUP BY "ImmutableProviderBotId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Нельзя закрепить MAX-бота за организацией: найдены повторяющиеся идентификаторы ботов.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaxBots_ImmutableProviderBotId",
                table: "OrganizationMaxBots",
                column: "ImmutableProviderBotId",
                unique: true,
                filter: "\"ImmutableProviderBotId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationMaxBots_ImmutableProviderBotId",
                table: "OrganizationMaxBots");
        }
    }
}
