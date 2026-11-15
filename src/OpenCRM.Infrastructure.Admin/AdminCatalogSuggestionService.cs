using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис работы с предложениями по справочнику техники в админке. </summary>
public sealed class AdminCatalogSuggestionService(
    OpenCRMContext opencrmContext,
    IMemoryCache memoryCache) : IAdminCatalogSuggestionService
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость кэша в памяти. </summary>
    private readonly IMemoryCache _memoryCache = memoryCache;

    /// <summary> Возвращает предложения по справочнику с фильтром по статусу. </summary>
    public async Task<IReadOnlyList<AdminCatalogSuggestionDto>> GetAsync(VehicleCatalogSuggestionStatus? status)
    {
        var query = this._opencrmContext.VehicleCatalogSuggestions.AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(suggestion => suggestion.Status == status.Value);
        }

        return await query
            .OrderBy(suggestion => suggestion.Status)
            .ThenByDescending(suggestion => suggestion.CreatedAt)
            .Select(suggestion => new AdminCatalogSuggestionDto
            {
                Id = suggestion.Id,
                Kind = suggestion.Kind,
                KindText = GetVehicleKindText(suggestion.Kind),
                BrandName = suggestion.BrandName,
                ModelName = suggestion.ModelName,
                Status = suggestion.Status,
                StatusText = GetStatusText(suggestion.Status),
                SuggestedByUserId = suggestion.SuggestedByUserId,
                SuggestedByEmail = this._opencrmContext.ApplicationUsers
                    .Where(user => user.Id == suggestion.SuggestedByUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                CreatedAt = suggestion.CreatedAt,
                ReviewedAt = suggestion.ReviewedAt,
                ReviewedByUserId = suggestion.ReviewedByUserId,
                AdminComment = suggestion.AdminComment,
                ApprovedBrandId = suggestion.ApprovedBrandId,
                ApprovedModelId = suggestion.ApprovedModelId
            })
            .ToListAsync();
    }

    /// <summary> Согласует предложение по справочнику техники. </summary>
    public async Task<AdminOperationResult> ApproveAsync(int id, string adminUserId, string? comment)
    {
        var executionStrategy = this._opencrmContext.Database.CreateExecutionStrategy();
        var operation = await executionStrategy.ExecuteAsync(async () =>
        {
            this._opencrmContext.ChangeTracker.Clear();
            await using var transaction = await this._opencrmContext.Database.BeginTransactionAsync();
            var result = await this.ApprovePendingAsync(id, adminUserId, comment);
            if (!result.Result.Succeeded)
            {
                await transaction.RollbackAsync();

                return result;
            }

            await transaction.CommitAsync();

            return result;
        });

        if (operation.Kind.HasValue)
        {
            this._memoryCache.Remove($"brand_options_{(int)operation.Kind.Value}");
            this._memoryCache.Remove("brand_options_0");
        }

        return operation.Result;
    }

    private async Task<(AdminOperationResult Result, VehicleKind? Kind)> ApprovePendingAsync(
        int id,
        string adminUserId,
        string? comment)
    {
        var suggestion = await this._opencrmContext.VehicleCatalogSuggestions
            .FirstOrDefaultAsync(item => item.Id == id);
        if (suggestion == null)
        {
            return (AdminOperationResult.Missing("Предложение не найдено."), null);
        }

        if (suggestion.Status != VehicleCatalogSuggestionStatus.Pending)
        {
            return (AdminOperationResult.Invalid("Обработать можно только предложение на модерации."), null);
        }

        var brand = await this._opencrmContext.Brands
            .Where(item => !item.IsDeleted)
            .FirstOrDefaultAsync(item =>
                item.Kind == suggestion.Kind &&
                item.Name == suggestion.BrandName);

        if (brand == null)
        {
            brand = new Brand
            {
                Name = suggestion.BrandName,
                Kind = suggestion.Kind
            };

            await this._opencrmContext.Brands.AddAsync(brand);
            await this._opencrmContext.SaveChangesAsync();
        }

        var model = await this._opencrmContext.Models
            .Where(item => !item.IsDeleted && item.BrandId == brand.Id && item.Kind == suggestion.Kind)
            .FirstOrDefaultAsync(item => item.Name == suggestion.ModelName);

        if (model == null)
        {
            model = new Model
            {
                BrandId = brand.Id,
                Name = suggestion.ModelName,
                Kind = suggestion.Kind
            };

            await this._opencrmContext.Models.AddAsync(model);
            await this._opencrmContext.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var normalizedComment = NormalizeComment(comment);
        var brandNamePattern = EscapeLikePattern(suggestion.BrandName);
        var modelNamePattern = EscapeLikePattern(suggestion.ModelName);
        var duplicates = await this._opencrmContext.VehicleCatalogSuggestions
            .Where(item =>
                item.Kind == suggestion.Kind &&
                item.Status == VehicleCatalogSuggestionStatus.Pending &&
                item.BrandName == suggestion.BrandName &&
                item.ModelName == suggestion.ModelName)
            .ToListAsync();

        foreach (var item in duplicates)
        {
            item.Status = VehicleCatalogSuggestionStatus.Approved;
            item.ApprovedBrandId = brand.Id;
            item.ApprovedModelId = model.Id;
            item.ReviewedAt = now;
            item.ReviewedByUserId = adminUserId;
            item.AdminComment = normalizedComment;
        }

        var vehicles = await this._opencrmContext.Vehicles
            .Where(vehicle =>
                !vehicle.IsDeleted &&
                vehicle.ModelId == null &&
                vehicle.Kind == suggestion.Kind &&
                vehicle.ManualBrandName != null &&
                vehicle.ManualModelName != null &&
                EF.Functions.ILike(vehicle.ManualBrandName, brandNamePattern, "\\") &&
                EF.Functions.ILike(vehicle.ManualModelName, modelNamePattern, "\\"))
            .ToListAsync();

        foreach (var vehicle in vehicles)
        {
            vehicle.BrandId = brand.Id;
            vehicle.ModelId = model.Id;
            vehicle.ManualBrandName = null;
            vehicle.ManualModelName = null;
        }

        await this._opencrmContext.SaveChangesAsync();

        return (AdminOperationResult.Success("Предложение одобрено и добавлено в каталог."), suggestion.Kind);
    }

    /// <summary> Отклоняет предложение по справочнику техники. </summary>
    public async Task<AdminOperationResult> RejectAsync(int id, string adminUserId, string? comment)
    {
        var suggestion = await this._opencrmContext.VehicleCatalogSuggestions.FirstOrDefaultAsync(item => item.Id == id);
        if (suggestion == null)
        {
            return AdminOperationResult.Missing("Предложение не найдено.");
        }

        if (suggestion.Status != VehicleCatalogSuggestionStatus.Pending)
        {
            return AdminOperationResult.Invalid("Обработать можно только предложение на модерации.");
        }

        var now = DateTime.UtcNow;
        var normalizedComment = NormalizeComment(comment);
        var duplicates = await this._opencrmContext.VehicleCatalogSuggestions
            .Where(item =>
                item.Kind == suggestion.Kind &&
                item.Status == VehicleCatalogSuggestionStatus.Pending &&
                item.BrandName == suggestion.BrandName &&
                item.ModelName == suggestion.ModelName)
            .ToListAsync();

        foreach (var item in duplicates)
        {
            item.Status = VehicleCatalogSuggestionStatus.Rejected;
            item.ReviewedAt = now;
            item.ReviewedByUserId = adminUserId;
            item.AdminComment = normalizedComment;
        }

        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Предложение отклонено.");
    }

    /// <summary> Удаляет только выбранное предложение по справочнику техники. </summary>
    public async Task<AdminOperationResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var suggestion = await this._opencrmContext.VehicleCatalogSuggestions
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (suggestion == null)
        {
            return AdminOperationResult.Missing("Предложение не найдено.");
        }

        this._opencrmContext.VehicleCatalogSuggestions.Remove(suggestion);
        await this._opencrmContext.SaveChangesAsync(cancellationToken);

        return AdminOperationResult.Success("Предложение удалено.", id);
    }

    /// <summary> Нормализует комментарий администратора. </summary>
    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    /// <summary> Экранирует специальные символы шаблона поиска для сравнения введённого названия целиком. </summary>
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    /// <summary> Возвращает текстовое описание статуса предложения. </summary>
    private static string GetStatusText(VehicleCatalogSuggestionStatus status)
    {
        return status switch
        {
            VehicleCatalogSuggestionStatus.Pending => "На модерации",
            VehicleCatalogSuggestionStatus.Approved => "Одобрено",
            VehicleCatalogSuggestionStatus.Rejected => "Отклонено",
            _ => "Неизвестно"
        };
    }

    /// <summary> Возвращает текстовое описание вида техники. </summary>
    private static string GetVehicleKindText(VehicleKind kind)
    {
        return kind switch
        {
            VehicleKind.PassengerCar => "Легковой автомобиль",
            VehicleKind.Truck => "Грузовая техника",
            VehicleKind.Motorcycle => "Мотоцикл",
            VehicleKind.ATV => "Квадроцикл",
            VehicleKind.UTV => "Багги / UTV",
            VehicleKind.Snowmobile => "Снегоход",
            VehicleKind.JetSki => "Гидроцикл",
            VehicleKind.Boat => "Лодка / катер",
            VehicleKind.SpecialEquipment => "Спецтехника",
            VehicleKind.Trailer => "Прицеп",
            _ => "Другая техника"
        };
    }
}
