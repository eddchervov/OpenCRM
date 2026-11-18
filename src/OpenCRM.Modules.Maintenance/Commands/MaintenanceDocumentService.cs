using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using System.Data;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Управляет документами по обслуживанию. </summary>
internal sealed class MaintenanceDocumentService(
    IMaintenanceRepository maintenanceRepository,
    ISystemClock systemClock,
    OpenCRMContext context,
    MaintenanceAccess maintenanceAccess,
    MaintenanceHistoryWriter maintenanceHistoryWriter) : IMaintenanceDocumentService
{
    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Гарантирует наличие номера акта выполненных работ. </summary>
    public async Task<int> EnsureWorkActNumber(string userId, Guid organizationId, Guid maintenanceId)
    {
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var currentNumber = await context.Maintenance
            .AsNoTracking()
            .Where(x =>
                x.Id == maintenanceId
                && !x.IsDeleted
                && x.Vehicle.OrganizationId == organizationId
                && x.WorkActOrganizationId == organizationId)
            .Select(x => x.WorkActNumber)
            .FirstOrDefaultAsync();

        if (currentNumber.HasValue)
        {
            return currentNumber.Value;
        }

        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        var isRetry = false;
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            for (var attempt = 0; ; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var maintenance = await context.Maintenance
                    .Include(x => x.Vehicle)
                    .FirstOrDefaultAsync(x =>
                        x.Id == maintenanceId
                        && !x.IsDeleted
                        && x.Vehicle.OrganizationId == organizationId);

                if (maintenance == null)
                {
                    throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} в организации {organizationId}");
                }

                await context.Entry(maintenance).ReloadAsync();

                if (maintenance.Status != MaintenanceStatus.Completed)
                {
                    throw new Exception("Акт можно сформировать только для выполненного обслуживания.");
                }

                if (maintenance.WorkActNumber.HasValue
                    && maintenance.WorkActOrganizationId is Guid actOrganizationId
                    && actOrganizationId == organizationId)
                {
                    await transaction.CommitAsync();
                    return maintenance.WorkActNumber.Value;
                }

                var nextNumber = (await context.Maintenance
                    .Where(x => x.WorkActOrganizationId == organizationId && x.WorkActNumber.HasValue)
                    .MaxAsync(x => (int?)x.WorkActNumber) ?? 0) + 1;

                maintenance.WorkActOrganizationId = organizationId;
                maintenance.WorkActNumber = nextNumber;

                try
                {
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return nextNumber;
                }
                catch (DbUpdateException) when (attempt < 2)
                {
                    await transaction.RollbackAsync();
                    context.ChangeTracker.Clear();
                }
            }

        });
    }

    /// <summary> Регистрирует факт формирования документа в истории. </summary>
    public async Task TrackDocumentGenerated(string userId, Guid? organizationId, Guid maintenanceId, string title, string? description = null)
    {
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");

        await this._maintenanceHistoryWriter.AddHistoryEventAsync(
            userId,
            organizationId,
            maintenance.VehicleId,
            maintenance.Id,
            MaintenanceHistoryEventCodes.EventDocumentGenerated,
            title,
            description);
    }

    /// <summary> Добавляет документ к обслуживанию. </summary>
    public async Task<MaintenanceDocumentDto> AddDocument(string userId, Guid? organizationId, Guid maintenanceId, CreateMaintenanceDocumentDto dto)
    {
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceDocumentsManage);

        var maintenance = await context.Maintenance
            .Include(x => x.Vehicle)
            .FirstOrDefaultAsync(x => x.Id == maintenanceId && !x.IsDeleted);

        if (maintenance == null
            || (organizationId.HasValue
                ? maintenance.Vehicle.OrganizationId != organizationId.Value
                : maintenance.UserId != userId || maintenance.Vehicle.OrganizationId != null))
        {
            throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");
        }

        if (string.IsNullOrWhiteSpace(dto.StoragePath))
        {
            throw new InvalidOperationException("Не указан путь файла документа.");
        }

        var kind = MaintenanceDocumentService.NormalizeDocumentPart(dto.Kind, 64);
        var format = MaintenanceDocumentService.NormalizeDocumentPart(dto.Format, 16).ToUpperInvariant();
        var actorDisplayName = await this._maintenanceHistoryWriter.ResolveActorDisplayNameAsync(userId);
        var version = (await context.MaintenanceDocuments
            .Where(x => x.MaintenanceId == maintenanceId
                && x.Kind == kind
                && x.Format == format
                && x.IncludePhotos == dto.IncludePhotos
                && !x.IsDeleted)
            .MaxAsync(x => (int?)x.Version) ?? 0) + 1;

        var document = new MaintenanceDocument
        {
            MaintenanceId = maintenance.Id,
            VehicleId = maintenance.VehicleId,
            OrganizationId = organizationId,
            UserId = maintenance.UserId,
            CreatedByUserId = userId,
            CreatedByDisplayName = actorDisplayName,
            Kind = kind,
            Format = format,
            CurrencyCode = maintenance.CurrencyCode,
            IncludePhotos = dto.IncludePhotos,
            Title = MaintenanceTextRules.Truncate(dto.Title?.Trim(), 250) ?? "Документ обслуживания",
            FileName = MaintenanceTextRules.Truncate(dto.FileName?.Trim(), 260) ?? "maintenance-document",
            StoragePath = MaintenanceTextRules.Truncate(dto.StoragePath.Trim(), 500)!,
            ContentType = MaintenanceTextRules.Truncate(dto.ContentType?.Trim(), 120) ?? "application/octet-stream",
            Size = dto.Size,
            Version = version,
            CreatedAt = systemClock.UtcNow
        };

        context.MaintenanceDocuments.Add(document);
        await context.SaveChangesAsync();

        return MaintenanceDocumentService.MapDocumentDto(document);
    }

    /// <summary> Возвращает список документов обслуживания. </summary>
    public async Task<IReadOnlyList<MaintenanceDocumentDto>> GetDocuments(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var maintenanceExists = await context.Maintenance
            .AsNoTracking()
            .Where(x => x.Id == maintenanceId && !x.IsDeleted)
            .Where(x => organizationId.HasValue
                ? x.Vehicle.OrganizationId == organizationId.Value
                : x.UserId == userId && x.Vehicle.OrganizationId == null)
            .AnyAsync();

        if (!maintenanceExists)
        {
            throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");
        }

        return await context.MaintenanceDocuments
            .AsNoTracking()
            .Where(x => x.MaintenanceId == maintenanceId && x.Kind == "signed_document" && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MaintenanceDocumentDto
            {
                Id = x.Id,
                Kind = x.Kind,
                Format = x.Format,
                CurrencyCode = x.CurrencyCode,
                IncludePhotos = x.IncludePhotos,
                Title = x.Title,
                FileName = x.FileName,
                ContentType = x.ContentType,
                Size = x.Size,
                Version = x.Version,
                CreatedByDisplayName = x.CreatedByDisplayName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary> Возвращает документ обслуживания для скачивания. </summary>
    public async Task<MaintenanceDocumentDownloadDto> GetDocumentForDownload(string userId, Guid? organizationId, Guid documentId)
    {
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var document = await context.MaintenanceDocuments
            .AsNoTracking()
            .Where(x => x.Id == documentId && !x.IsDeleted && !x.Maintenance.IsDeleted)
            .Where(x => organizationId.HasValue
                ? x.Maintenance.Vehicle.OrganizationId == organizationId.Value
                : x.Maintenance.UserId == userId && x.Maintenance.Vehicle.OrganizationId == null)
            .Select(x => new MaintenanceDocumentDownloadDto
            {
                Id = x.Id,
                FileName = x.FileName,
                StoragePath = x.StoragePath,
                ContentType = x.ContentType
            })
            .FirstOrDefaultAsync();

        return document ?? throw new Exception($"Не найден документ с documentId={documentId} и userId={userId}");
    }

    /// <summary> Преобразует документ в DTO. </summary>
    private static MaintenanceDocumentDto MapDocumentDto(MaintenanceDocument document)
    {
        return new MaintenanceDocumentDto
        {
            Id = document.Id,
            Kind = document.Kind,
            Format = document.Format,
            CurrencyCode = document.CurrencyCode,
            IncludePhotos = document.IncludePhotos,
            Title = document.Title,
            FileName = document.FileName,
            ContentType = document.ContentType,
            Size = document.Size,
            Version = document.Version,
            CreatedByDisplayName = document.CreatedByDisplayName,
            CreatedAt = document.CreatedAt
        };
    }

    /// <summary> Нормализует часть имени документа с ограничением длины. </summary>
    private static string NormalizeDocumentPart(string value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Не указан тип документа.");
        }

        return MaintenanceTextRules.Truncate(normalized.ToLowerInvariant(), maxLength)!;
    }
}
