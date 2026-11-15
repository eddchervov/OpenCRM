using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Создаёт записи об обслуживании. </summary>
internal sealed class MaintenanceCreateService(
    IMaintenanceRepository maintenanceRepository,
    IVehicleMaintenanceLookupService vehicleLookup,
    IVehicleCounterCommandService vehicleService,
    ISystemClock systemClock,
    OpenCRMContext context,
    MaintenanceAccess maintenanceAccess,
    MaintenancePlanAccess maintenancePlanAccess,
    MaintenanceEmployeeResolver maintenanceEmployeeResolver,
    MaintenanceSlotGuard maintenanceSlotGuard,
    MaintenanceDetailRules maintenanceDetailRules,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceDefectLinks maintenanceDefectLinks,
    MaintenanceNotificationOutbox maintenanceNotificationOutbox,
    MaintenanceBuyerResolver maintenanceBuyerResolver,
    MaintenanceConsumption maintenanceConsumption,
    IMaintenanceSchedulerSyncService organizationSchedulerService,
    MaintenanceTransaction transaction) : IMaintenanceCreateService
{
    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Определение сотрудника для обслуживания. </summary>
    private readonly MaintenanceEmployeeResolver _maintenanceEmployeeResolver = maintenanceEmployeeResolver;

    /// <summary> Проверка конфликтов постов при назначении. </summary>
    private readonly MaintenanceSlotGuard _maintenanceSlotGuard = maintenanceSlotGuard;

    /// <summary> Правила работы со строками работ и услуг. </summary>
    private readonly MaintenanceDetailRules _maintenanceDetailRules = maintenanceDetailRules;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Синхронизация связанных дефектов. </summary>
    private readonly MaintenanceDefectLinks _maintenanceDefectLinks = maintenanceDefectLinks;

    /// <summary> Постановка уведомлений в очередь. </summary>
    private readonly MaintenanceNotificationOutbox _maintenanceNotificationOutbox = maintenanceNotificationOutbox;

    /// <summary> Определение покупателя по обслуживанию. </summary>
    private readonly MaintenanceBuyerResolver _maintenanceBuyerResolver = maintenanceBuyerResolver;

    /// <summary> Синхронизация расхода материалов по обслуживанию. </summary>
    private readonly MaintenanceConsumption _maintenanceConsumption = maintenanceConsumption;

    /// <summary> Синхронизация с планировщиком организации. </summary>
    private readonly IMaintenanceSchedulerSyncService _organizationSchedulerService = organizationSchedulerService;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Создаёт обслуживание для техники пользователя. </summary>
    public async Task<Guid> Create(string userId, Guid vehicleId, CreateMaintenanceDto dto)
    {
        return await this.Create(userId, null, vehicleId, dto);
    }

    /// <summary> Создаёт обслуживание с указанием организации. </summary>
    public async Task<Guid> Create(string userId, Guid? organizationId, Guid vehicleId, CreateMaintenanceDto dto)
    {
        return await this.Create(userId, organizationId, vehicleId, dto, null);
    }

    /// <summary> Создаёт обслуживание со связью с планировщиком. </summary>
    public async Task<Guid> Create(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        CreateMaintenanceDto dto,
        MaintenanceSchedulerLink? schedulerLink)
    {
        var maintenanceId = Guid.NewGuid();
        await this._transaction.ExecuteAsync(
            async () =>
            {
                await this.CreateCoreAsync(userId, organizationId, vehicleId, dto, maintenanceId, schedulerLink);
            },
            () => context.Maintenance.AsNoTracking().AnyAsync(maintenance => maintenance.Id == maintenanceId));

        return maintenanceId;
    }

    /// <summary> Выполняет создание записи обслуживания. </summary>
    private async Task<Guid> CreateCoreAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        CreateMaintenanceDto dto,
        Guid maintenanceId,
        MaintenanceSchedulerLink? schedulerLink)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);

        var vehicle = await vehicleLookup.FindAsync(userId, organizationId, vehicleId)
            ?? throw new Exception($"У пользователя userId={userId}, не найдена техника с vehicleId={vehicleId}");

        var linkedDefectIds = dto.LinkedDefectIds ?? [];
        if (organizationId.HasValue
            && schedulerLink is { SourceEntityId: { } sourceDefectId }
            && string.Equals(schedulerLink.SourceCode, "defect", StringComparison.OrdinalIgnoreCase))
        {
            linkedDefectIds = linkedDefectIds.Append(sourceDefectId).Distinct().ToArray();
        }

        await this._maintenanceDefectLinks.EnsureLinkedDefectsBelongToVehicleAsync(organizationId, vehicleId, linkedDefectIds);

        var maintenance = new MaintenanceEntity
        {
            Id = maintenanceId,
            VehicleId = vehicleId,
            Comment = MaintenanceTextRules.NormalizeOptional(dto.Comment),
            DateTimeCreation = systemClock.UtcNow,
            Location = MaintenanceTextRules.NormalizeOptional(dto.Location),
            EmployeeId = organizationId.HasValue ? dto.EmployeeId : null,
            Performer = organizationId.HasValue
                ? await this._maintenanceEmployeeResolver.ResolveEmployeeNameAsync(organizationId.Value, dto.EmployeeId)
                : MaintenanceTextRules.NormalizeOptional(dto.Performer),
            Mileage = dto.Mileage,
            Status = dto.Status,
            Type = dto.Type,
            UserId = userId,
            Details = MaintenanceDetailRules.MapDetails(dto.Details)
        };
        if (organizationId.HasValue)
        {
            var buyerClientId = dto.HasBuyerSelection ? dto.BuyerClientId : dto.BuyerClientId ?? vehicle.OrganizationClientId;
            await this._maintenanceBuyerResolver.SetBuyerAsync(maintenance, organizationId.Value, buyerClientId);
        }

        MaintenanceDetailRules.AssignDetailIds(maintenance.Details);
        await maintenancePlanAccess.EnsureCanCreateAsync(userId, organizationId, dto.EmployeeId, maintenance.Details);
        await this._maintenanceSlotGuard.EnsureCanAssignLocationAsync(organizationId, maintenance.Location);
        await this._maintenanceDetailRules.ValidateServiceDetailsAsync(organizationId, maintenance.Details);
        if (organizationId.HasValue && dto.PaidAmount.GetValueOrDefault() != 0)
        {
            throw new InvalidOperationException("Сначала создайте запись, затем примите оплату в её карточке.");
        }

        MaintenanceMoneyRules.ApplyFinancialFields(maintenance, dto.DiscountAmount, dto.PaidAmount, dto.LaborCostAmount);

        MaintenanceDateRules.ApplyStatusAndDates(maintenance, dto.Status, dto.PlannedDate, dto.CompletedDate);
        MaintenanceDateRules.ApplyPlannedTime(maintenance, dto.PlannedStartTime, dto.PlannedEndTime, dto.PlannedEndDate ?? maintenance.PlannedEndDate);
        await this._maintenanceSlotGuard.EnsureNoWorkspaceSlotConflictAsync(organizationId, maintenance);

        await maintenanceRepository.Add(maintenance);
        await maintenanceRepository.Save();
        if (organizationId.HasValue)
        {
            await this._maintenanceConsumption.SyncMaterialConsumptionAsync(userId, organizationId.Value, maintenance);
            maintenanceRepository.Update(maintenance);
            await maintenanceRepository.Save();
        }

        await this._maintenanceHistoryWriter.AddHistoryEventAsync(
            userId,
            organizationId,
            maintenance.VehicleId,
            maintenance.Id,
            MaintenanceHistoryEventCodes.EventCreated,
            "Обслуживание создано",
            MaintenanceChangeRules.BuildMaintenanceSummary(maintenance));
        await this._maintenanceDefectLinks.SyncLinkedDefectsAsync(
            userId,
            organizationId,
            vehicleId,
            maintenance.Id,
            dto.Status,
            linkedDefectIds);
        await vehicleService.SyncCounterFromMaintenanceAsync(
            userId,
            organizationId,
            vehicleId,
            maintenance.Id,
            dto.Status == MaintenanceStatus.Completed ? dto.Mileage : null,
            maintenance.Date);

        if (organizationId.HasValue
            && !string.IsNullOrWhiteSpace(schedulerLink?.SourceCode)
            && !string.IsNullOrWhiteSpace(schedulerLink.TriggerKey))
        {
            await this._organizationSchedulerService.LinkMaintenanceTaskAsync(
                userId,
                organizationId.Value,
                vehicleId,
                maintenance.Id,
                schedulerLink.SourceCode,
                schedulerLink.SourceEntityId,
                schedulerLink.TriggerKey,
                (int)maintenance.Status);
        }

        if (organizationId.HasValue && maintenance.Status == MaintenanceStatus.Planned)
        {
            await this._maintenanceNotificationOutbox.QueueMaxNotificationAsync(organizationId.Value, maintenance, "booking_created");
            await this._maintenanceNotificationOutbox.QueueTelegramNotificationAsync(organizationId.Value, maintenance, "booking_created");
        }

        return maintenance.Id;
    }
}
