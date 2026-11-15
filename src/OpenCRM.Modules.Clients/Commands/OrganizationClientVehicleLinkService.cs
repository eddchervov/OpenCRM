using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Сервис связей клиентов с техникой организации. </summary>
internal sealed class OrganizationClientVehicleLinkService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    ClientsAccess clientsAccess,
    OrganizationClientTransaction transaction,
    OrganizationClientHistoryWriter historyWriter) : IOrganizationClientVehicleLinkService
{
    /// <summary> Отвязывает технику от клиента организации. </summary>
    public async Task UnlinkVehicleAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsVehiclesManage);

        Guid? savedHistoryId = null;
        await transaction.ExecuteAsync(
            async () =>
            {
                savedHistoryId = await this.UnlinkVehicleCoreAsync(userId, organizationId, clientId, vehicleId);
            },
            async () => savedHistoryId.HasValue && await context.OrganizationClientHistoryEvents.AsNoTracking()
                .AnyAsync(history => history.Id == savedHistoryId.Value));
    }

    /// <summary> Передаёт технику другому клиенту организации. </summary>
    public async Task TransferVehicleAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId, Guid targetClientId)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsVehiclesManage);

        if (targetClientId == clientId)
        {
            throw new InvalidOperationException("Выберите другого клиента.");
        }

        var targetClientExists = await context.OrganizationClients
            .AsNoTracking()
            .AnyAsync(client => client.Id == targetClientId && client.OrganizationId == organizationId && !client.IsDeleted);
        if (!targetClientExists)
        {
            throw new InvalidOperationException("Новый клиент не найден.");
        }

        Guid? savedHistoryId = null;
        await transaction.ExecuteAsync(
            async () =>
            {
                savedHistoryId = await this.TransferVehicleCoreAsync(userId, organizationId, clientId, vehicleId, targetClientId);
            },
            async () => savedHistoryId.HasValue && await context.OrganizationClientHistoryEvents.AsNoTracking()
                .AnyAsync(history => history.Id == savedHistoryId.Value));
    }

    /// <summary> Внутренняя логика отвязки техники от клиента. </summary>
    private async Task<Guid> UnlinkVehicleCoreAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId)
    {
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(existingVehicle => existingVehicle.Id == vehicleId
                && existingVehicle.OrganizationId == organizationId
                && existingVehicle.OrganizationClientId == clientId
                && !existingVehicle.IsDeleted)
            ?? throw new InvalidOperationException("Техника клиента не найдена.");

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Vehicles" WHERE "Id" = {vehicleId} FOR UPDATE""");
        await context.Entry(vehicle).ReloadAsync();
        await this.LoadVehicleDisplayReferencesAsync(vehicle);
        OrganizationClientVehicleLinkService.EnsureVehicleBelongsToClient(vehicle, organizationId, clientId);

        var vehicleTitle = ClientRules.BuildVehicleDisplayName(
            vehicle.BrandId.HasValue ? vehicle.Brand!.Name : (vehicle.ManualBrandName ?? string.Empty),
            vehicle.ModelId.HasValue ? vehicle.Model!.Name : (vehicle.ManualModelName ?? string.Empty),
            vehicle.YearIssue,
            vehicle.Name,
            vehicle.RegistrationPlate);

        vehicle.OrganizationClientId = null;
        var now = DateTime.UtcNow;
        var historyId = historyWriter.AddHistory(clientId, organizationId, userId, "vehicle_unlinked", "Техника отвязана от клиента", vehicleTitle, "vehicle", vehicleId, now);
        await historyWriter.AddAuditAsync(organizationId, userId, "clients.vehicle.unlinked", "Техника отвязана от клиента", vehicleTitle, clientId);
        await context.SaveChangesAsync();

        return historyId;
    }

    /// <summary> Внутренняя логика передачи техники другому клиенту. </summary>
    private async Task<Guid> TransferVehicleCoreAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId, Guid targetClientId)
    {
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(existingVehicle => existingVehicle.Id == vehicleId
                && existingVehicle.OrganizationId == organizationId
                && existingVehicle.OrganizationClientId == clientId
                && !existingVehicle.IsDeleted)
            ?? throw new InvalidOperationException("Техника клиента не найдена.");

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Vehicles" WHERE "Id" = {vehicleId} FOR UPDATE""");
        await context.Entry(vehicle).ReloadAsync();
        await this.LoadVehicleDisplayReferencesAsync(vehicle);
        OrganizationClientVehicleLinkService.EnsureVehicleBelongsToClient(vehicle, organizationId, clientId);

        var targetClient = await context.OrganizationClients
            .FirstOrDefaultAsync(existingClient => existingClient.Id == targetClientId
                && existingClient.OrganizationId == organizationId
                && !existingClient.IsDeleted)
            ?? throw new InvalidOperationException("Новый клиент не найден.");
        var sourceClientExists = await context.OrganizationClients.AsNoTracking()
            .AnyAsync(existingClient => existingClient.Id == clientId && existingClient.OrganizationId == organizationId);
        if (!sourceClientExists)
        {
            throw new InvalidOperationException("Техника клиента не найдена.");
        }

        var vehicleTitle = ClientRules.BuildVehicleDisplayName(
            vehicle.BrandId.HasValue ? vehicle.Brand!.Name : (vehicle.ManualBrandName ?? string.Empty),
            vehicle.ModelId.HasValue ? vehicle.Model!.Name : (vehicle.ManualModelName ?? string.Empty),
            vehicle.YearIssue,
            vehicle.Name,
            vehicle.RegistrationPlate);

        var now = DateTime.UtcNow;
        vehicle.OrganizationClientId = targetClientId;
        var historyId = historyWriter.AddHistory(clientId, organizationId, userId, "vehicle_transferred_out", "Техника перенесена другому клиенту", $"{vehicleTitle} -> {targetClient.Name}", "vehicle", vehicleId, now);
        historyWriter.AddHistory(targetClientId, organizationId, userId, "vehicle_transferred_in", "Техника перенесена клиенту", vehicleTitle, "vehicle", vehicleId, now);
        await historyWriter.AddAuditAsync(organizationId, userId, "clients.vehicle.transferred", "Техника перенесена между клиентами", $"{vehicleTitle} -> {targetClient.Name}", clientId);
        await context.SaveChangesAsync();

        return historyId;
    }

    /// <summary> Загружает данные для отображения техники. </summary>
    private async Task LoadVehicleDisplayReferencesAsync(Vehicle vehicle)
    {
        await context.Entry(vehicle).Reference(existingVehicle => existingVehicle.Brand).Query().LoadAsync();
        await context.Entry(vehicle).Reference(existingVehicle => existingVehicle.Model).Query().LoadAsync();
    }

    /// <summary> Проверяет принадлежность техники клиенту организации. </summary>
    private static void EnsureVehicleBelongsToClient(Vehicle vehicle, Guid organizationId, Guid clientId)
    {
        if (vehicle.OrganizationId is not Guid currentOrganizationId || currentOrganizationId != organizationId
            || vehicle.OrganizationClientId is not Guid currentClientId || currentClientId != clientId || vehicle.IsDeleted)
        {
            throw new InvalidOperationException("Техника клиента не найдена.");
        }
    }
}
