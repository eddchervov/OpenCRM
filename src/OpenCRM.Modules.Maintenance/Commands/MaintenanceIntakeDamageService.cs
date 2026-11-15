using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Сохраняет отметки осмотра без изменения статуса заказа. </summary>
internal sealed class MaintenanceIntakeDamageService(
    OpenCRMContext context,
    IMaintenanceRepository maintenanceRepository,
    MaintenanceAccess access,
    MaintenanceOrderLoader orderLoader,
    MaintenanceTransaction transaction,
    MaintenanceHistoryWriter historyWriter,
    ISystemClock clock) : IMaintenanceIntakeDamageService
{
    public async Task<MaintenanceIntakeDamagesDto> GetDamages(string userId, Guid organizationId, Guid maintenanceId)
    {
        await access.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new InvalidOperationException("Обслуживание не найдено.");

        var result = await context.Maintenance.AsNoTracking().AsSingleQuery()
            .Where(maintenance => maintenance.Id == maintenanceId && !maintenance.IsDeleted)
            .Select(maintenance => new MaintenanceIntakeDamagesDto
            {
                Version = maintenance.Version,
                DiagramVersion = maintenance.IntakeDiagramVersion ?? MaintenanceIntakeDiagramVersions.Current,
                Damages = context.MaintenanceIntakeDamages
                    .Where(damage => damage.MaintenanceId == maintenance.Id && !damage.IsDeleted)
                    .OrderBy(damage => damage.Number)
                    .Select(damage => new MaintenanceIntakeDamageDto
                    {
                        Id = damage.Id,
                        Number = damage.Number,
                        ViewCode = damage.ViewCode,
                        DamageType = damage.DamageType,
                        X = damage.X,
                        Y = damage.Y,
                        Description = damage.Description,
                        PhotoIds = damage.Photos.Where(link => !link.Photo.IsDeleted)
                            .OrderBy(link => link.Photo.CreatedAt).ThenBy(link => link.PhotoId)
                            .Select(link => link.PhotoId).ToList()
                    }).ToList()
            }).SingleAsync();

        for (var index = 0; index < result.Damages.Count; index++)
        {
            result.Damages[index].Number = index + 1;
        }

        return result;
    }

    public async Task SaveDamage(
        string userId, Guid organizationId, Guid maintenanceId, Guid damageId, SaveMaintenanceIntakeDamageDto dto)
    {
        await access.EnsureIntakeCanWriteAsync(userId, organizationId);
        MaintenanceIntakeDamageService.ValidateDamage(damageId, dto);
        var description = MaintenanceTextRules.NormalizeOptional(dto.Description);
        uint? savedVersion = null;

        await transaction.ExecuteAsync(async () =>
        {
            var maintenance = await orderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
            MaintenanceIntakeDamageService.EnsureVersion(maintenance, dto.Version);
            if (maintenance.IntakeDiagramVersion != null && maintenance.IntakeDiagramVersion != dto.DiagramVersion)
            {
                throw new DbUpdateConcurrencyException("Схема осмотра отличается от открытой. Обновите осмотр перед выбором места повреждения.");
            }

            maintenance.IntakeDiagramVersion ??= dto.DiagramVersion;
            var photoIds = dto.PhotoIds.Distinct().Order().ToArray();
            if (photoIds.Length > 0)
            {
                var photos = await context.MaintenancePhotos.FromSqlInterpolated(
                    $"""SELECT * FROM "MaintenancePhotos" WHERE "Id" = ANY ({photoIds}) ORDER BY "Id" FOR UPDATE""")
                    .AsNoTracking().ToListAsync();

                if (photos.Count != photoIds.Length || photos.Any(photo => photo.MaintenanceId != maintenanceId
                    || photo.IsDeleted || photo.Stage != MaintenancePhotoStage.Before))
                {
                    throw new InvalidOperationException("Для повреждения доступны только фотографии «До работы» этого заказа.");
                }
            }

            var damage = await context.MaintenanceIntakeDamages.Include(value => value.Photos)
                .SingleOrDefaultAsync(value => value.Id == damageId);

            if (damage != null && (damage.MaintenanceId != maintenanceId || damage.IsDeleted))
            {
                throw new InvalidOperationException("Повреждение не найдено.");
            }

            var previous = damage == null ? null : MaintenanceIntakeDamageService.Describe(damage);
            if (damage == null)
            {
                var lastNumber = await context.MaintenanceIntakeDamages
                    .Where(value => value.MaintenanceId == maintenanceId)
                    .Select(value => (int?)value.Number).MaxAsync() ?? 0;

                damage = new MaintenanceIntakeDamage
                {
                    Id = damageId, MaintenanceId = maintenanceId, Number = checked(lastNumber + 1)
                };
                context.MaintenanceIntakeDamages.Add(damage);
            }

            damage.ViewCode = dto.ViewCode;
            damage.DamageType = dto.DamageType;
            damage.X = dto.X;
            damage.Y = dto.Y;
            damage.Description = description;
            var removedLinks = damage.Photos.Where(link => !photoIds.Contains(link.PhotoId)).ToArray();
            context.MaintenanceIntakeDamagePhotos.RemoveRange(removedLinks);
            var existingPhotoIds = damage.Photos.Select(link => link.PhotoId).ToHashSet();
            foreach (var photoId in photoIds.Where(photoId => !existingPhotoIds.Contains(photoId)))
            {
                damage.Photos.Add(new MaintenanceIntakeDamagePhoto
                {
                    MaintenanceId = maintenanceId, DamageId = damage.Id, PhotoId = photoId
                });
            }

            maintenance.IntakeRecordedAt ??= clock.UtcNow;
            // Обновляем xmin заказа и для изменений дочерних строк осмотра.
            context.Entry(maintenance).Property(value => value.IntakeRecordedAt).IsModified = true;
            var current = MaintenanceIntakeDamageService.Describe(damage, photoIds);
            await historyWriter.AddHistoryEventAsync(
                userId, organizationId, maintenance.VehicleId, maintenanceId,
                "intake_damage_saved", previous == null ? "Добавлено повреждение при приёмке" : "Уточнено повреждение при приёмке",
                previous == null ? current : $"Было: {previous}\nСтало: {current}",
                payloadJson: JsonSerializer.Serialize(new { damageId = damage.Id, diagramVersion = maintenance.IntakeDiagramVersion }));
            savedVersion = maintenance.Version;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(maintenance => maintenance.Id == maintenanceId
            && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    public async Task DeleteDamage(string userId, Guid organizationId, Guid maintenanceId, Guid damageId, uint version)
    {
        await access.EnsureIntakeCanWriteAsync(userId, organizationId);
        uint? savedVersion = null;

        await transaction.ExecuteAsync(async () =>
        {
            var maintenance = await orderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
            MaintenanceIntakeDamageService.EnsureVersion(maintenance, version);
            if (!MaintenanceIntakeDiagramVersions.IsSupported(maintenance.IntakeDiagramVersion))
            {
                throw new InvalidOperationException("Сохранённая версия схемы осмотра недоступна. Изменение повреждений заблокировано.");
            }

            var damage = await context.MaintenanceIntakeDamages.Include(value => value.Photos)
                .SingleOrDefaultAsync(value => value.Id == damageId && value.MaintenanceId == maintenanceId && !value.IsDeleted)
                ?? throw new InvalidOperationException("Повреждение не найдено.");

            damage.IsDeleted = true;
            context.Entry(maintenance).Property(value => value.IntakeRecordedAt).IsModified = true;
            await historyWriter.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenanceId,
                "intake_damage_deleted", "Удалено повреждение при приёмке", MaintenanceIntakeDamageService.Describe(damage),
                payloadJson: JsonSerializer.Serialize(new { damageId = damage.Id, diagramVersion = maintenance.IntakeDiagramVersion }));
            savedVersion = maintenance.Version;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(maintenance => maintenance.Id == maintenanceId
            && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    private static void EnsureVersion(MaintenanceEntity maintenance, uint version)
    {
        if (maintenance.Version != version)
        {
            throw new DbUpdateConcurrencyException("Осмотр обновлён после открытия. Проверьте сохранённые данные перед продолжением.")
            {
                Data =
                {
                    ["ExpectedVersion"] = version,
                    ["ActualVersion"] = maintenance.Version
                }
            };
        }
    }

    private static void ValidateDamage(Guid damageId, SaveMaintenanceIntakeDamageDto dto)
    {
        if (!MaintenanceIntakeDiagramVersions.IsSupported(dto.DiagramVersion))
        {
            throw new InvalidOperationException("Версия схемы осмотра не поддерживается. Обновите страницу.");
        }

        if (damageId == Guid.Empty || dto.ViewCode is not ("front" or "rear" or "left" or "right")
            || dto.DamageType is not ("scratch" or "chip" or "dent" or "other"))
        {
            throw new InvalidOperationException("Выберите ракурс и тип повреждения.");
        }

        if (!double.IsFinite(dto.X) || !double.IsFinite(dto.Y) || dto.X is < 0 or > 1 || dto.Y is < 0 or > 1)
        {
            throw new InvalidOperationException("Отметка должна находиться внутри схемы автомобиля.");
        }

        if (dto.PhotoIds == null || dto.PhotoIds.Count > 100 || dto.PhotoIds.Contains(Guid.Empty))
        {
            throw new InvalidOperationException("Выберите не более 100 фотографий повреждения.");
        }
    }

    private static string Describe(MaintenanceIntakeDamage damage, IEnumerable<Guid>? photoIds = null)
    {
        var view = damage.ViewCode switch { "front" => "спереди", "rear" => "сзади", "left" => "левый борт", _ => "правый борт" };
        var type = damage.DamageType switch { "scratch" => "царапина", "chip" => "скол", "dent" => "вмятина", _ => "другое" };
        var photos = string.Join(", ", (photoIds ?? damage.Photos.Select(link => link.PhotoId)).Order());

        return FormattableString.Invariant($"{view}, {type}, ({damage.X:0.###}; {damage.Y:0.###}): {damage.Description}. Фото: {photos}");
    }
}
