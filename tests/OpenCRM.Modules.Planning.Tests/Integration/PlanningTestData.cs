using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Planning.Tests.Integration;

internal static class PlanningTestData
{
    internal static async Task<Vehicle> CreateVehicleAsync(OpenCRMContext context, Organization organization, ApplicationUser owner)
    {
        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            User = owner,
            ManualBrandName = "Тест",
            ManualModelName = "Автомобиль",
            RegistrationPlate = $"Т{Random.Shared.Next(100, 999)}СТ",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return vehicle;
    }

    internal static async Task<OrganizationClient> CreateClientAsync(OpenCRMContext context, Organization organization)
    {
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = $"Клиент {Guid.NewGuid():N}" };
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();

        return client;
    }

    internal static async Task<VehicleDefect> CreateDefectAsync(
        OpenCRMContext context,
        Organization organization,
        Vehicle vehicle,
        ApplicationUser owner)
    {
        var defect = new VehicleDefect
        {
            OrganizationId = organization.Id,
            VehicleId = vehicle.Id,
            User = owner,
            Title = "Проверить крепление"
        };
        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();

        return defect;
    }

    internal static async Task<OpenCRM.Domain.Maintenance> CreateMaintenanceAsync(
        OpenCRMContext context,
        Vehicle vehicle,
        ApplicationUser owner)
    {
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            VehicleId = vehicle.Id,
            User = owner,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Type = MaintenanceType.Repair,
            Status = MaintenanceStatus.Planned
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        return maintenance;
    }

    internal static OrganizationSchedulerTask CreateTask(
        Organization organization,
        ApplicationUser owner,
        string title,
        DateTime? startAt = null,
        DateTime? reminderAt = null)
    {
        var createdAt = DateTime.UtcNow;

        return new OrganizationSchedulerTask
        {
            OrganizationId = organization.Id,
            CreatedByUserId = owner.Id,
            Title = title,
            StartAt = startAt,
            ReminderAt = reminderAt,
            ProcessCode = "detailing",
            WorkflowStageCode = "new",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }
}
