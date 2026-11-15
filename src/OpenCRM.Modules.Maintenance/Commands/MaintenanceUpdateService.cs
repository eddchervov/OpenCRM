using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Обновляет записи об обслуживании и их состав работ. </summary>
internal sealed class MaintenanceUpdateService(
    IMaintenanceRepository maintenanceRepository,
    IVehicleCounterCommandService vehicleService,
    OpenCRMContext context,
    MaintenanceOrderLoader maintenanceOrderLoader,
    MaintenanceAccess maintenanceAccess,
    MaintenancePlanAccess maintenancePlanAccess,
    MaintenanceEmployeeResolver maintenanceEmployeeResolver,
    MaintenanceWorkGuard maintenanceWorkGuard,
    MaintenanceSlotGuard maintenanceSlotGuard,
    MaintenanceDetailRules maintenanceDetailRules,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceDefectLinks maintenanceDefectLinks,
    MaintenanceNotificationOutbox maintenanceNotificationOutbox,
    MaintenanceBuyerResolver maintenanceBuyerResolver,
    MaintenanceConsumption maintenanceConsumption,
    IMaintenanceSchedulerSyncService organizationSchedulerService,
    MaintenanceTransaction transaction) : IMaintenanceUpdateService
{
    /// <summary> Загрузчик записи обслуживания с блокировкой. </summary>
    private readonly MaintenanceOrderLoader _maintenanceOrderLoader = maintenanceOrderLoader;

    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Проверка платных полей тарифа обслуживания. </summary>
    private readonly MaintenancePlanAccess _maintenancePlanAccess = maintenancePlanAccess;

    /// <summary> Определение сотрудника для обслуживания. </summary>
    private readonly MaintenanceEmployeeResolver _maintenanceEmployeeResolver = maintenanceEmployeeResolver;

    /// <summary> Проверки выполнения работ при изменениях. </summary>
    private readonly MaintenanceWorkGuard _maintenanceWorkGuard = maintenanceWorkGuard;

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

    /// <summary> Обновляет состав работ и услуг обслуживания. </summary>
    public Task UpdateServices(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceServicesDto dto)
    {
        uint? savedVersion = null;

        return this._transaction.ExecuteAsync(
            async () =>
            {
                savedVersion = await this.UpdateServicesCoreAsync(userId, organizationId, maintenanceId, dto);
            },
            () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
                maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Обновляет ставки НДС по работам обслуживания. </summary>
    public Task UpdateVatRates(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceVatRatesDto dto)
    {
        uint? savedVersion = null;

        return this._transaction.ExecuteAsync(
            async () =>
            {
                savedVersion = await this.UpdateVatRatesCoreAsync(userId, organizationId, maintenanceId, dto);
            },
            () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
                maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет обновление ставок НДС. </summary>
    private async Task<uint?> UpdateVatRatesCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        UpdateMaintenanceVatRatesDto dto)
    {
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        var maintenance = await this._maintenanceOrderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
        if (dto.Version != maintenance.Version)
        {
            throw new InvalidOperationException("Заказ уже изменён. Обновите карточку.");
        }

        var pricedDetails = maintenance.Details.Where(detail => detail.Price.HasValue).ToList();
        if (dto.Lines == null || dto.Lines.Count != pricedDetails.Count
            || dto.Lines.Any(line => line == null)
            || dto.Lines.Select(line => line.LineIndex).Distinct().Count() != dto.Lines.Count
            || dto.Lines.Any(line => !MaintenanceVatRates.IsValid(line.VatRateCode))
            || dto.Lines.Any(line => line.LineIndex < 0 || line.LineIndex >= pricedDetails.Count))
        {
            throw new InvalidOperationException("Укажите ставку НДС для каждой оплачиваемой работы.");
        }

        var changes = new List<string>();
        foreach (var line in dto.Lines)
        {
            var detail = pricedDetails[line.LineIndex];
            if (detail.VatRateCode == line.VatRateCode)
            {
                continue;
            }

            changes.Add($"{detail.Name}: {detail.VatRateCode ?? "не указана"} → {line.VatRateCode}");
            detail.VatRateCode = line.VatRateCode;
        }

        if (changes.Count == 0)
        {
            return null;
        }

        // Отмечаем родителя, чтобы проверка версии заказа охватила изменения строк.
        context.Entry(maintenance).Property(item => item.Comment).IsModified = true;
        await context.SaveChangesAsync();
        await this._maintenanceHistoryWriter.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenanceId,
            "vat_rates_corrected", "Уточнены ставки НДС", string.Join("; ", changes));

        return maintenance.Version;
    }

    /// <summary> Выполняет обновление состава работ и услуг. </summary>
    private async Task<uint?> UpdateServicesCoreAsync(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceServicesDto dto)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var maintenance = await this._maintenanceOrderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
        if (dto.Version != maintenance.Version)
        {
            throw new InvalidOperationException("Запись уже изменена. Обновите карточку и проверьте состав услуг.");
        }
        if (maintenance.HandedOverAt.HasValue || maintenance.Status == MaintenanceStatus.Cancelled)
        {
            throw new InvalidOperationException("Для изменения услуг сначала верните запись в работу.");
        }
        if (dto.Details == null || dto.Details.Count > 100)
        {
            throw new InvalidOperationException("В записи допускается не более 100 строк услуг.");
        }

        var services = MaintenanceDetailRules.MapDetails(dto.Details);
        MaintenanceDetailRules.AssignDetailIds(services, maintenance.Details.Where(item => !MaintenanceDetailRules.IsMaterialDetail(item)).ToList());
        await this._maintenanceWorkGuard.ValidateStartedWorkAsync(maintenance, services, maintenance.EmployeeId);
        var canLeavePricesUnspecified = dto.AllowUnspecifiedPrices
            && maintenance.Status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress;

        if (services.Any(item => !item.Price.HasValue) && !canLeavePricesUnspecified)
        {
            throw new InvalidOperationException("Укажите цену каждой услуги. Для бесплатной услуги укажите 0.");
        }
        if (services.Any(MaintenanceDetailRules.IsMaterialDetail))
        {
            throw new InvalidOperationException("Материалы изменяются отдельно от услуг.");
        }
        await this._maintenancePlanAccess.PreserveRestrictedTeamFieldsAsync(
            userId, organizationId, maintenance, services, maintenance.EmployeeId);
        await this._maintenanceDetailRules.ValidateServiceDetailsAsync(organizationId, services, maintenance.Details);
        var previous = MaintenanceSnapshot.From(maintenance);
        maintenance.ReplaceDetails(services.Concat(maintenance.Details.Where(MaintenanceDetailRules.IsMaterialDetail)));
        if (!MaintenanceChangeRules.HasMaintenanceChanges(previous, maintenance))
        {
            return null;
        }

        // Отмечаем родителя, чтобы проверка версии заказа охватила изменения строк.
        context.Entry(maintenance).Property(item => item.Comment).IsModified = true;
        await context.SaveChangesAsync();
        await this._maintenanceHistoryWriter.AddUpdateHistoryEventsAsync(userId, organizationId, maintenance, previous);

        return maintenance.Version;
    }

    /// <summary> Обновляет обслуживание пользователя. </summary>
    public async Task Update(string userId, Guid maintenanceId, UpdateMaintenanceDto dto)
    {
        await this.Update(userId, null, maintenanceId, dto);
    }

    /// <summary> Обновляет обслуживание с учётом организации. </summary>
    public async Task Update(string userId, Guid? organizationId, Guid maintenanceId, UpdateMaintenanceDto dto)
    {
        uint? savedVersion = null;
        await this._transaction.ExecuteAsync(
            async () =>
            {
                savedVersion = await this.UpdateCoreAsync(userId, organizationId, maintenanceId, dto);
                if (organizationId.HasValue)
                {
                    await this._organizationSchedulerService.SyncMaintenanceWorkflowAsync(
                        userId, organizationId.Value, maintenanceId, (int)dto.Status);
                }
            },
            () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
                maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет обновление записи обслуживания. </summary>
    private async Task<uint?> UpdateCoreAsync(string userId, Guid? organizationId, Guid maintenanceId, UpdateMaintenanceDto dto)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");

        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenance.Id} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new InvalidOperationException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        if (dto.Version.HasValue && dto.Version.Value != maintenance.Version)
        {
            throw new InvalidOperationException("Запись уже изменена. Обновите страницу перед сохранением.");
        }

        if (organizationId.HasValue)
        {
            MaintenanceWorkflowRules.ValidateWorkspaceStatusChange(maintenance.Status, dto.Status);
            await this._maintenanceWorkGuard.EnsureWorkAllowsStatusChangeAsync(maintenance, dto.Status);
        }

        var previous = MaintenanceSnapshot.From(maintenance);
        var buyerChanged = organizationId.HasValue && dto.ChangeBuyer
            && !Nullable.Equals(dto.BuyerClientId, maintenance.BuyerClientId);
        if (buyerChanged)
        {
            await this._maintenanceBuyerResolver.SetBuyerAsync(maintenance, organizationId!.Value, dto.BuyerClientId);
        }

        var details = MaintenanceDetailRules.MapDetails(dto.Details);
        MaintenanceDetailRules.AssignDetailIds(details, maintenance.Details);
        if (organizationId.HasValue)
        {
            var previousDetailsById = maintenance.Details.ToDictionary(detail => detail.DetailId);
            foreach (var detail in details)
            {
                previousDetailsById.TryGetValue(detail.DetailId, out var previousDetail);
                detail.MaterialUnitCost = previousDetail == null ? 0 : previousDetail.MaterialUnitCost;
                detail.MaterialTotalCost = previousDetail == null ? 0 : previousDetail.MaterialTotalCost;
            }
        }

        var planUpdate = await this._maintenancePlanAccess.PreserveRestrictedTeamFieldsAsync(
            userId, organizationId, maintenance, details, dto.EmployeeId);
        var employeeId = planUpdate;
        await this._maintenanceWorkGuard.ValidateStartedWorkAsync(maintenance, details, employeeId);
        await this._maintenanceDetailRules.ValidateServiceDetailsAsync(organizationId, details, maintenance.Details);

        var location = MaintenanceTextRules.NormalizeOptional(dto.Location);
        await this._maintenanceSlotGuard.EnsureCanAssignLocationAsync(organizationId, location, maintenance.Location);
        maintenance.Comment = MaintenanceTextRules.NormalizeOptional(dto.Comment);
        maintenance.Location = location;
        maintenance.Performer = organizationId.HasValue
            ? await this._maintenanceEmployeeResolver.ResolveEmployeeNameAsync(organizationId.Value, employeeId, maintenance.EmployeeId, maintenance.Performer)
            : MaintenanceTextRules.NormalizeOptional(dto.Performer);
        maintenance.EmployeeId = organizationId.HasValue ? employeeId : null;
        maintenance.Mileage = dto.Mileage;
        maintenance.Status = dto.Status;
        maintenance.Type = dto.Type;
        maintenance.ReplaceDetails(details);
        MaintenanceMoneyRules.ApplyFinancialFields(maintenance, dto.DiscountAmount, organizationId.HasValue ? maintenance.PaidAmount : dto.PaidAmount, dto.LaborCostAmount);
        MaintenanceDateRules.ApplyStatusAndDates(maintenance, dto.Status, dto.PlannedDate, dto.CompletedDate);
        MaintenanceDateRules.ApplyPlannedTime(maintenance, dto.PlannedStartTime, dto.PlannedEndTime, dto.PlannedEndDate ?? maintenance.PlannedEndDate);
        await this._maintenanceSlotGuard.EnsureCanAssignLocationAsync(
            organizationId,
            maintenance.Location,
            previous.Location,
            requireActiveSameLocation: MaintenanceSlotGuard.RequiresActivePost(previous, maintenance));
        await this._maintenanceSlotGuard.EnsureNoWorkspaceSlotConflictAsync(organizationId, maintenance);

        var hasMaintenanceChanges = MaintenanceChangeRules.HasMaintenanceChanges(previous, maintenance) || buyerChanged;
        var hasLinkedDefectChanges = await this._maintenanceHistoryWriter.HasLinkedDefectChangesAsync(organizationId, maintenance.Id, dto.LinkedDefectIds);
        if (!hasMaintenanceChanges && !hasLinkedDefectChanges)
        {
            return null;
        }

        if (hasMaintenanceChanges)
        {
            maintenanceRepository.Update(maintenance);
            await maintenanceRepository.Save();
            if (organizationId.HasValue)
            {
                await this._maintenanceConsumption.SyncMaterialConsumptionAsync(userId, organizationId.Value, maintenance);
                maintenanceRepository.Update(maintenance);
                await maintenanceRepository.Save();
            }

            await this._maintenanceHistoryWriter.AddUpdateHistoryEventsAsync(
                userId,
                organizationId,
                maintenance,
                previous);
            if (buyerChanged)
            {
                await this._maintenanceHistoryWriter.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenance.Id,
                    "buyer_changed", "Покупатель изменён", maintenance.BuyerName);
            }
        }
        else
        {
            context.Entry(maintenance).State = EntityState.Unchanged;
            // Версия родителя подтверждает фиксацию изменений связей при повторе транзакции.
            context.Entry(maintenance).Property(item => item.Comment).IsModified = true;
        }

        if (dto.LinkedDefectIds != null && hasLinkedDefectChanges)
        {
            await this._maintenanceDefectLinks.SyncLinkedDefectsAsync(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                dto.Status,
                dto.LinkedDefectIds);
        }
        else if (previous.Status != MaintenanceStatus.Completed && dto.Status == MaintenanceStatus.Completed)
        {
            await this._maintenanceDefectLinks.CloseLinkedDefectsAsync(maintenance.Id);
        }

        if (hasMaintenanceChanges)
        {
            await vehicleService.SyncCounterFromMaintenanceAsync(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                dto.Status == MaintenanceStatus.Completed ? dto.Mileage : null,
                maintenance.Date);

            if (organizationId.HasValue)
            {
                await this._maintenanceNotificationOutbox.QueueMaxChangeAsync(organizationId.Value, maintenance, previous);
                await this._maintenanceNotificationOutbox.QueueTelegramChangeAsync(organizationId.Value, maintenance, previous);
            }
        }

        return maintenance.Version;
    }
}
