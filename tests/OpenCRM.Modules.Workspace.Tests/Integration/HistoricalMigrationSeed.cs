using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

internal static class HistoricalMigrationSeed
{
    public static async Task InsertOrganizationAsync(OpenCRMContext context, Organization organization)
    {
        // Заполняем историческую схему без полей организации из более поздних миграций.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Organizations"
                ("Id", "Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({organization.Id}, {organization.Name}, {organization.PrimaryScenarioCode},
                {organization.OwnerUserId}, {organization.IsActive}, {organization.CreatedAt}, {organization.UpdatedAt})
            """);
        context.Attach(organization);
    }
}
