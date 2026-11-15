using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Сохраняет данные приёмки по обслуживанию. </summary>
internal sealed class MaintenanceIntakeService(
    IMaintenanceRepository maintenanceRepository,
    ISystemClock systemClock,
    OpenCRMContext context,
    MaintenanceAccess maintenanceAccess,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceTransaction transaction) : IMaintenanceIntakeService
{
    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Сохраняет данные приёмки по обслуживанию. </summary>
    public Task SaveIntake(string userId, Guid organizationId, Guid maintenanceId, SaveMaintenanceIntakeDto dto)
    {
        uint? savedVersion = null;
        return this._transaction.ExecuteAsync(async () =>
        {
            await this.SaveIntakeCoreAsync(userId, organizationId, maintenanceId, dto);
            savedVersion = context.Maintenance.Local.Single(maintenance => maintenance.Id == maintenanceId).Version;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
            maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет сохранение данных приёмки. </summary>
    private async Task SaveIntakeCoreAsync(string userId, Guid organizationId, Guid maintenanceId, SaveMaintenanceIntakeDto dto)
    {
        await this._maintenanceAccess.EnsureIntakeCanWriteAsync(userId, organizationId);

        var completeness = MaintenanceTextRules.NormalizeOptional(dto.Completeness);
        var visibleDamage = MaintenanceTextRules.NormalizeOptional(dto.VisibleDamage);
        var notes = MaintenanceTextRules.NormalizeOptional(dto.Notes);
        if (dto.Mileage < 0)
        {
            throw new InvalidOperationException("Пробег при приёмке не может быть отрицательным.");
        }

        if (!dto.Mileage.HasValue && completeness is null && visibleDamage is null && notes is null)
        {
            throw new InvalidOperationException("Заполните хотя бы одно поле акта приёмки.");
        }

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new InvalidOperationException("Обслуживание не найдено.");
        if (maintenance.Vehicle.MileageUnit == VehicleMileageUnit.Kilometers && dto.Mileage is > 9999999)
        {
            throw new InvalidOperationException("Пробег при приёмке не может превышать 9 999 999 км.");
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenance.Id} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new InvalidOperationException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);

        if (maintenance.IntakeRecordedAt.HasValue
            && maintenance.IntakeMileage == dto.Mileage
            && maintenance.IntakeCompleteness == completeness
            && maintenance.IntakeVisibleDamage == visibleDamage
            && maintenance.IntakeNotes == notes)
        {
            return;
        }

        var isFirstIntake = !maintenance.IntakeRecordedAt.HasValue;
        var previous = MaintenanceIntakeService.FormatIntake(maintenance);
        maintenance.IntakeMileage = dto.Mileage;
        maintenance.IntakeCompleteness = completeness;
        maintenance.IntakeVisibleDamage = visibleDamage;
        maintenance.IntakeNotes = notes;
        maintenance.IntakeRecordedAt ??= systemClock.UtcNow;
        maintenance.IntakeDiagramVersion ??= MaintenanceIntakeDiagramVersions.Current;

        await this._maintenanceHistoryWriter.AddHistoryEventAsync(
            userId, organizationId, maintenance.VehicleId, maintenance.Id,
            "intake_saved", isFirstIntake ? "Приёмка зафиксирована" : "Приёмка уточнена",
            isFirstIntake
                ? MaintenanceIntakeService.FormatIntake(maintenance)
                : $"Было: {previous}\nСтало: {MaintenanceIntakeService.FormatIntake(maintenance)}");
    }

    /// <summary> Формирует текстовое описание приёмки по записи обслуживания. </summary>
    private static string FormatIntake(MaintenanceEntity maintenance)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(maintenance.IntakeNotes))
        {
            parts.Add(maintenance.IntakeNotes);
        }

        if (maintenance.IntakeMileage.HasValue)
        {
            parts.Add($"Пробег: {maintenance.IntakeMileage.Value}");
        }

        if (!string.IsNullOrWhiteSpace(maintenance.IntakeCompleteness))
        {
            parts.Add($"Комплектность: {maintenance.IntakeCompleteness}");
        }

        if (!string.IsNullOrWhiteSpace(maintenance.IntakeVisibleDamage))
        {
            parts.Add($"Повреждения: {maintenance.IntakeVisibleDamage}");
        }

        return parts.Count == 0 ? "не заполнено" : string.Join("; ", parts);
    }
}
