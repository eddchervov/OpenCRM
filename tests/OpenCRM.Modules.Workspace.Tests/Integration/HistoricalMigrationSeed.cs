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

    public static async Task InsertVehicleAsync(OpenCRMContext context, Vehicle vehicle)
    {
        var organizationId = vehicle.Organization?.Id ?? vehicle.OrganizationId;
        vehicle.OrganizationId = organizationId;
        vehicle.DateTimeCreation = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Vehicles"
                ("Id", "Kind", "MileageUnit", "ManualBrandName", "ManualModelName", "RegistrationPlate",
                 "DateTimeCreation", "IsDeleted", "IsArchived", "UserId", "OrganizationId")
            VALUES ({vehicle.Id}, {(int)vehicle.Kind}, {(int)vehicle.MileageUnit}, {vehicle.ManualBrandName},
                {vehicle.ManualModelName}, {vehicle.RegistrationPlate}, {vehicle.DateTimeCreation},
                {vehicle.IsDeleted}, {vehicle.IsArchived}, {vehicle.UserId}, {organizationId})
            """);
        context.Attach(vehicle);
    }
}
