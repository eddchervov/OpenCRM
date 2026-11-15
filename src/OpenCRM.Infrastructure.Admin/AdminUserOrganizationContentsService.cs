using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис загрузки содержимого организации пользователя. </summary>
internal sealed class AdminUserOrganizationContentsService(OpenCRMContext opencrmContext)
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает содержимое организации пользователя. </summary>
    public async Task<AdminUserOrganizationContentsDto?> GetOrganizationContentsAsync(string userId, Guid organizationId)
    {
        var organization = await this._opencrmContext.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId
                && (item.OwnerUserId == userId || item.Members.Any(member => member.UserId == userId)))
            .Select(item => new { item.Id, item.Name })
            .FirstOrDefaultAsync();

        if (organization == null)
        {
            return null;
        }

        var members = await this._opencrmContext.OrganizationMembers
            .AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.User.Email)
            .Select(member => new AdminUserOrganizationMemberDto
            {
                UserId = member.UserId,
                Name = member.User.Email ?? member.User.PhoneNumber ?? member.UserId,
                RoleName = AdminUserRules.FormatOrganizationRole(member.RoleCode)
            })
            .ToListAsync();

        var vehicles = await this._opencrmContext.Vehicles
            .AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
            .OrderBy(vehicle => vehicle.Name)
            .Select(vehicle => new AdminUserOrganizationVehicleDto
            {
                Id = vehicle.Id,
                Name = vehicle.Name ?? vehicle.RegistrationPlate ?? vehicle.Vin ?? "Техника",
                ClientName = vehicle.OrganizationClient != null ? vehicle.OrganizationClient.Name : null
            })
            .ToListAsync();

        var clients = await this._opencrmContext.OrganizationClients
            .AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted)
            .OrderBy(client => client.Name)
            .Select(client => new AdminUserOrganizationClientDto
            {
                Id = client.Id,
                Name = client.Name,
                VehiclesCount = client.Vehicles.Count(vehicle => !vehicle.IsDeleted)
            })
            .ToListAsync();

        var maintenancesCount = await this._opencrmContext.Maintenance
            .CountAsync(maintenance => maintenance.Vehicle.OrganizationId == organizationId && !maintenance.IsDeleted);
        var recentMaintenances = await this._opencrmContext.Maintenance
            .AsNoTracking()
            .Where(maintenance => maintenance.Vehicle.OrganizationId == organizationId && !maintenance.IsDeleted)
            .OrderByDescending(maintenance => maintenance.Date)
            .Select(maintenance => new AdminUserOrganizationMaintenanceDto
            {
                Id = maintenance.Id,
                Date = maintenance.Date,
                VehicleName = maintenance.Vehicle.Name ?? maintenance.Vehicle.RegistrationPlate ?? maintenance.Vehicle.Vin ?? "Техника",
                StatusName = maintenance.Status.ToString()
            })
            .Take(20)
            .ToListAsync();

        return new AdminUserOrganizationContentsDto
        {
            Id = organization.Id,
            Name = organization.Name,
            Members = members,
            Vehicles = vehicles,
            Clients = clients,
            MaintenancesCount = maintenancesCount,
            RecentMaintenances = recentMaintenances
        };
    }
}
