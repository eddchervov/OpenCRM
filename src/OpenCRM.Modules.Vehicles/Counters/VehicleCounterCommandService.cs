using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Counters;

/// <summary> Изменяет показания счетчиков техники. </summary>
public sealed class VehicleCounterCommandService : IVehicleCounterCommandService
{
    /// <summary> Репозиторий техники для работы со счётчиками. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Системные часы для фиксации времени операций. </summary>
    private readonly ISystemClock _systemClock;

    /// <summary> Контекст базы данных для работы со счётчиками. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Сервис подписки организации для проверок доступа. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Записывает события в историю техники. </summary>
    private readonly VehicleHistoryWriter _historyWriter;

    /// <summary> Создаёт сервис команд счётчиков техники с зависимостями. </summary>
    public VehicleCounterCommandService(
        IVehicleRepository vehicleRepository,
        ISystemClock systemClock,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        VehicleHistoryWriter historyWriter)
    {
        this._vehicleRepository = vehicleRepository;
        this._systemClock = systemClock;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._historyWriter = historyWriter;
    }

    /// <summary> Обновляет или добавляет показание счётчика техники. </summary>
    public async Task UpdateCounterAsync(string userId, Guid? organizationId, UpdateVehicleCounterDto dto)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersManage);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, dto.VehicleId)
            ?? throw new InvalidOperationException("Техника не найдена.");

        if (dto.Value < 0)
        {
            throw new InvalidOperationException("Значение счетчика не может быть отрицательным.");
        }

        var recordedAt = dto.RecordedAt == default ? this._systemClock.UtcNow : dto.RecordedAt;
        var startOfDay = recordedAt.Date;
        var endOfDay = startOfDay.AddDays(1);
        var normalizedComment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();

        VehicleCounterEntry? existingEntry;

        if (dto.EntryId.HasValue)
        {
            existingEntry = await this._context.VehicleCounterEntries
                .FirstOrDefaultAsync(entry => !entry.IsDeleted
                    && entry.Id == dto.EntryId.Value
                    && entry.VehicleId == vehicle.Id
                    && entry.SourceType == VehicleCounterSourceTypes.Manual);

            if (existingEntry == null)
            {
                throw new InvalidOperationException("Ручная запись не найдена или уже недоступна.");
            }

            var conflictingEntry = await this._context.VehicleCounterEntries
                .FirstOrDefaultAsync(entry => !entry.IsDeleted
                    && entry.Id != existingEntry.Id
                    && entry.VehicleId == vehicle.Id
                    && entry.SourceType == VehicleCounterSourceTypes.Manual
                    && entry.RecordedAt >= startOfDay
                    && entry.RecordedAt < endOfDay);

            if (conflictingEntry != null)
            {
                throw new InvalidOperationException("На эту дату уже есть другая ручная запись.");
            }
        }
        else
        {
            existingEntry = await this._context.VehicleCounterEntries
                .FirstOrDefaultAsync(entry => !entry.IsDeleted
                    && entry.VehicleId == vehicle.Id
                    && entry.SourceType == VehicleCounterSourceTypes.Manual
                    && entry.RecordedAt >= startOfDay
                    && entry.RecordedAt < endOfDay);
        }

        var isNewEntry = existingEntry == null;
        var previousCounterValue = existingEntry?.Value;
        var previousCounterDate = existingEntry?.RecordedAt;

        if (existingEntry == null)
        {
            existingEntry = new VehicleCounterEntry
            {
                VehicleId = vehicle.Id,
                UserId = userId,
                SourceType = VehicleCounterSourceTypes.Manual
            };
            this._context.VehicleCounterEntries.Add(existingEntry);
        }

        existingEntry.Value = dto.Value;
        existingEntry.RecordedAt = recordedAt;
        existingEntry.Comment = normalizedComment;
        existingEntry.IsDeleted = false;
        existingEntry.DeletionDate = null;

        await this.RecalculateCurrentCounterAsync(vehicle);
        await this._vehicleRepository.Save();
        await this._historyWriter.AddAsync(
            userId,
            organizationId,
            vehicle.Id,
            isNewEntry ? "counter_created" : "counter_updated",
            isNewEntry ? "Добавлена запись счетчика" : "Изменена запись счетчика",
            VehicleCounterCommandService.BuildCounterHistoryDescription(
                vehicle.MileageUnit,
                previousCounterValue,
                previousCounterDate,
                existingEntry.Value,
                existingEntry.RecordedAt),
            "counter",
            existingEntry.Id);
    }

    /// <summary> Удаляет запись показания счётчика. </summary>
    public async Task DeleteCounterEntryAsync(string userId, Guid? organizationId, Guid entryId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersManage);

        var entry = await this._context.VehicleCounterEntries
            .Include(value => value.Vehicle)
            .FirstOrDefaultAsync(value => !value.IsDeleted
                && value.Id == entryId
                && value.SourceType == VehicleCounterSourceTypes.Manual
                && (organizationId.HasValue
                    ? value.Vehicle.OrganizationId == organizationId.Value
                    : value.Vehicle.UserId == userId && value.Vehicle.OrganizationId == null)
                && !value.Vehicle.IsDeleted);

        if (entry == null)
        {
            throw new InvalidOperationException("Ручная запись не найдена или уже удалена.");
        }

        entry.Delete(this._systemClock.UtcNow);
        await this.RecalculateCurrentCounterAsync(entry.Vehicle);
        await this._vehicleRepository.Save();
        await this._historyWriter.AddAsync(
            userId,
            organizationId,
            entry.VehicleId,
            "counter_deleted",
            "Удалена запись счетчика",
            VehicleCounterCommandService.BuildCounterHistoryDescription(entry.Vehicle.MileageUnit, entry.Value, entry.RecordedAt, null, null),
            "counter",
            entry.Id);
    }

    /// <summary> Синхронизирует счётчик техники со значением из обслуживания. </summary>
    public async Task SyncCounterFromMaintenanceAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId,
        int? value,
        DateTime recordedAt)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new InvalidOperationException("Техника не найдена.");

        var existingEntry = await this._context.VehicleCounterEntries
            .FirstOrDefaultAsync(entry => entry.MaintenanceId == maintenanceId && !entry.IsDeleted);

        if (value == null)
        {
            if (existingEntry != null)
            {
                existingEntry.Delete(this._systemClock.UtcNow);
            }

            await this.RecalculateCurrentCounterAsync(vehicle);
            await this._vehicleRepository.Save();

            return;
        }

        if (value.Value < 0)
        {
            throw new InvalidOperationException("Значение счетчика не может быть отрицательным.");
        }

        if (existingEntry == null)
        {
            existingEntry = new VehicleCounterEntry
            {
                VehicleId = vehicleId,
                UserId = userId,
                MaintenanceId = maintenanceId,
                SourceType = VehicleCounterSourceTypes.Maintenance
            };
            this._context.VehicleCounterEntries.Add(existingEntry);
        }

        existingEntry.Value = value.Value;
        existingEntry.RecordedAt = recordedAt;
        existingEntry.Comment = null;
        existingEntry.SourceType = VehicleCounterSourceTypes.Maintenance;
        existingEntry.IsDeleted = false;
        existingEntry.DeletionDate = null;

        await this.RecalculateCurrentCounterAsync(vehicle);
        await this._vehicleRepository.Save();
    }

    /// <summary> Отменяет синхронизацию счётчика с обслуживанием. </summary>
    public async Task RemoveCounterSyncFromMaintenanceAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new InvalidOperationException("Техника не найдена.");

        var entry = await this._context.VehicleCounterEntries
            .FirstOrDefaultAsync(value => value.MaintenanceId == maintenanceId && !value.IsDeleted);

        if (entry != null)
        {
            entry.Delete(this._systemClock.UtcNow);
        }

        await this.RecalculateCurrentCounterAsync(vehicle);
        await this._vehicleRepository.Save();
    }

    /// <summary> Проверяет право пользователя изменять данные рабочего пространства. </summary>
    private async Task EnsureCanWriteWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.VehiclesEdit)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId.Value,
            permissionCode);
    }

    /// <summary> Пересчитывает текущее показание счётчика техники. </summary>
    private async Task RecalculateCurrentCounterAsync(Vehicle vehicle)
    {
        var trackedEntries = this._context.ChangeTracker.Entries<VehicleCounterEntry>()
            .Where(entry => entry.Entity.VehicleId == vehicle.Id
                && entry.State != EntityState.Unchanged)
            .ToList();
        var changedEntryIds = trackedEntries.Select(entry => entry.Entity.Id).ToArray();
        var latestPersistedEntry = await this._context.VehicleCounterEntries
            .AsNoTracking()
            .Where(entry => entry.VehicleId == vehicle.Id && !entry.IsDeleted
                && !changedEntryIds.Contains(entry.Id))
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .FirstOrDefaultAsync();
        var latestTrackedEntry = trackedEntries
            .Where(entry => entry.State != EntityState.Deleted
                && !entry.Entity.IsDeleted)
            .Select(entry => entry.Entity)
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .FirstOrDefault();
        var latestEntry = new[] { latestPersistedEntry, latestTrackedEntry }
            .Where(entry => entry != null)
            .OrderByDescending(entry => entry!.RecordedAt)
            .ThenByDescending(entry => entry!.Id)
            .FirstOrDefault();

        vehicle.CurrentMileage = latestEntry?.Value;
        vehicle.CurrentMileageUpdatedAt = latestEntry?.RecordedAt;
        this._vehicleRepository.Update(vehicle);
    }

    /// <summary> Формирует описание изменения счётчика для истории. </summary>
    private static string BuildCounterHistoryDescription(
        VehicleMileageUnit mileageUnit,
        int? previousValue,
        DateTime? previousDate,
        int? currentValue,
        DateTime? currentDate)
    {
        var unit = mileageUnit == VehicleMileageUnit.EngineHours ? "м/ч" : "км";
        var current = currentValue.HasValue
            ? $"{currentValue.Value:N0} {unit} от {VehicleCounterCommandService.FormatDate(currentDate!.Value)}"
            : "удалено";

        if (!previousValue.HasValue)
        {
            return $"Значение: {current}";
        }

        var previous = $"{previousValue.Value:N0} {unit} от {VehicleCounterCommandService.FormatDate(previousDate!.Value)}";

        return $"Значение: {previous} -> {current}";
    }

    /// <summary> Форматирует дату для описания истории. </summary>
    private static string FormatDate(DateTime value)
    {
        return value.Date.ToString("dd.MM.yyyy");
    }
}
