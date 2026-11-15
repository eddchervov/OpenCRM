using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис управления справочником марок и моделей техники. </summary>
public sealed class AdminVehicleCatalogService(OpenCRMContext opencrmContext) : IAdminVehicleCatalogService
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает марки техники с фильтрами и поиском. </summary>
    public async Task<IReadOnlyList<AdminCatalogBrandDto>> GetBrandsAsync(VehicleKind? kind, string? search)
    {
        var query = this._opencrmContext.Brands.Where(brand => !brand.IsDeleted);

        if (kind.HasValue)
        {
            query = query.Where(brand => brand.Kind == kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(brand => EF.Functions.ILike(brand.Name, $"%{normalizedSearch}%"));
        }

        return await query
            .OrderBy(brand => brand.Kind)
            .ThenBy(brand => brand.Name)
            .Select(brand => new AdminCatalogBrandDto
            {
                Id = brand.Id,
                Name = brand.Name,
                LogoKey = brand.LogoKey,
                Kind = brand.Kind,
                KindText = GetKindText(brand.Kind),
                ModelsCount = this._opencrmContext.Models.Count(model =>
                    !model.IsDeleted &&
                    model.BrandId == brand.Id),
                VehiclesCount = this._opencrmContext.Vehicles.Count(vehicle =>
                    !vehicle.IsDeleted &&
                    vehicle.BrandId == brand.Id)
            })
            .ToListAsync();
    }

    /// <summary> Создаёт марку техники. </summary>
    public async Task<AdminOperationResult> CreateBrandAsync(AdminSaveBrandDto dto)
    {
        if (dto.LogoKey is { Length: > 64 })
        {
            return AdminOperationResult.Invalid("Ключ логотипа не должен превышать 64 символа.");
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return AdminOperationResult.Invalid("Укажите название бренда.");
        }

        var alreadyExists = await this._opencrmContext.Brands.AnyAsync(brand =>
            !brand.IsDeleted &&
            brand.Kind == dto.Kind &&
            brand.Name == name);
        if (alreadyExists)
        {
            return AdminOperationResult.Invalid("Такой бренд уже есть для выбранного типа техники.");
        }

        var brand = new Brand
        {
            Name = name,
            LogoKey = NormalizeName(dto.LogoKey),
            Kind = dto.Kind
        };

        this._opencrmContext.Brands.Add(brand);
        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Бренд добавлен.", brand.Id);
    }

    /// <summary> Обновляет марку техники. </summary>
    public async Task<AdminOperationResult> UpdateBrandAsync(int brandId, AdminSaveBrandDto dto)
    {
        var brand = await this._opencrmContext.Brands.FirstOrDefaultAsync(item =>
            item.Id == brandId &&
            !item.IsDeleted);
        if (brand == null)
        {
            return AdminOperationResult.Missing("Бренд не найден.");
        }

        if (dto.LogoKey is { Length: > 64 })
        {
            return AdminOperationResult.Invalid("Ключ логотипа не должен превышать 64 символа.");
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return AdminOperationResult.Invalid("Укажите название бренда.");
        }

        var duplicateExists = await this._opencrmContext.Brands.AnyAsync(item =>
            !item.IsDeleted &&
            item.Id != brandId &&
            item.Kind == dto.Kind &&
            item.Name == name);
        if (duplicateExists)
        {
            return AdminOperationResult.Invalid("Такой бренд уже есть для выбранного типа техники.");
        }

        var isUsed = await this._opencrmContext.Vehicles.AnyAsync(vehicle =>
            !vehicle.IsDeleted &&
            vehicle.BrandId == brandId);
        if (isUsed && brand.Kind != dto.Kind)
        {
            return AdminOperationResult.Invalid("Нельзя изменить тип бренда, который уже используется в технике.");
        }

        if (dto.LogoKey != null)
        {
            brand.LogoKey = NormalizeName(dto.LogoKey);
        }

        brand.Name = name;
        brand.Kind = dto.Kind;

        var models = await this._opencrmContext.Models
            .Where(model => !model.IsDeleted && model.BrandId == brandId)
            .ToListAsync();

        foreach (var model in models)
        {
            model.Kind = dto.Kind;
        }

        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Бренд сохранен.");
    }

    /// <summary> Удаляет марку техники. </summary>
    public async Task<AdminOperationResult> DeleteBrandAsync(int brandId)
    {
        var brand = await this._opencrmContext.Brands.FirstOrDefaultAsync(item =>
            item.Id == brandId &&
            !item.IsDeleted);
        if (brand == null)
        {
            return AdminOperationResult.Missing("Бренд не найден.");
        }

        var isUsed = await this._opencrmContext.Vehicles.AnyAsync(vehicle =>
            !vehicle.IsDeleted &&
            vehicle.BrandId == brandId);
        if (isUsed)
        {
            return AdminOperationResult.Invalid("Нельзя удалить бренд, который используется в технике.");
        }

        var models = await this._opencrmContext.Models
            .Where(model => !model.IsDeleted && model.BrandId == brandId)
            .ToListAsync();

        brand.IsDeleted = true;
        foreach (var model in models)
        {
            model.IsDeleted = true;
        }

        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Бренд и его модели удалены.");
    }

    /// <summary> Возвращает модели марки с поиском. </summary>
    public async Task<AdminCatalogModelsResultDto> GetModelsAsync(int brandId, string? search)
    {
        var brandExists = await this._opencrmContext.Brands.AnyAsync(brand =>
            brand.Id == brandId &&
            !brand.IsDeleted);
        if (!brandExists)
        {
            return new AdminCatalogModelsResultDto
            {
                NotFound = true,
                ErrorMessage = "Бренд не найден."
            };
        }

        var query = this._opencrmContext.Models.Where(model =>
            !model.IsDeleted &&
            model.BrandId == brandId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(model => EF.Functions.ILike(model.Name, $"%{normalizedSearch}%"));
        }

        var models = await query
            .OrderBy(model => model.Name)
            .Select(model => new AdminCatalogModelDto
            {
                Id = model.Id,
                BrandId = model.BrandId,
                Name = model.Name,
                Kind = model.Kind,
                KindText = GetKindText(model.Kind),
                VehiclesCount = this._opencrmContext.Vehicles.Count(vehicle =>
                    !vehicle.IsDeleted &&
                    vehicle.ModelId == model.Id)
            })
            .ToListAsync();

        return new AdminCatalogModelsResultDto
        {
            Models = models
        };
    }

    /// <summary> Создаёт модель техники для марки. </summary>
    public async Task<AdminOperationResult> CreateModelAsync(int brandId, AdminSaveModelDto dto)
    {
        var brand = await this._opencrmContext.Brands.FirstOrDefaultAsync(item =>
            item.Id == brandId &&
            !item.IsDeleted);
        if (brand == null)
        {
            return AdminOperationResult.Missing("Бренд не найден.");
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return AdminOperationResult.Invalid("Укажите название модели.");
        }

        var alreadyExists = await this._opencrmContext.Models.AnyAsync(model =>
            !model.IsDeleted &&
            model.BrandId == brandId &&
            model.Kind == brand.Kind &&
            model.Name == name);
        if (alreadyExists)
        {
            return AdminOperationResult.Invalid("Такая модель уже есть у выбранного бренда.");
        }

        var model = new Model
        {
            BrandId = brand.Id,
            Kind = brand.Kind,
            Name = name
        };

        this._opencrmContext.Models.Add(model);
        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Модель добавлена.", model.Id);
    }

    /// <summary> Обновляет модель техники. </summary>
    public async Task<AdminOperationResult> UpdateModelAsync(int modelId, AdminSaveModelDto dto)
    {
        var model = await this._opencrmContext.Models
            .Include(item => item.Brand)
            .FirstOrDefaultAsync(item => item.Id == modelId && !item.IsDeleted && !item.Brand.IsDeleted);
        if (model == null)
        {
            return AdminOperationResult.Missing("Модель не найдена.");
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return AdminOperationResult.Invalid("Укажите название модели.");
        }

        var duplicateExists = await this._opencrmContext.Models.AnyAsync(item =>
            !item.IsDeleted &&
            item.Id != modelId &&
            item.BrandId == model.BrandId &&
            item.Kind == model.Kind &&
            item.Name == name);
        if (duplicateExists)
        {
            return AdminOperationResult.Invalid("Такая модель уже есть у выбранного бренда.");
        }

        model.Name = name;
        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Модель сохранена.");
    }

    /// <summary> Удаляет модель техники. </summary>
    public async Task<AdminOperationResult> DeleteModelAsync(int modelId)
    {
        var model = await this._opencrmContext.Models.FirstOrDefaultAsync(item =>
            item.Id == modelId &&
            !item.IsDeleted);
        if (model == null)
        {
            return AdminOperationResult.Missing("Модель не найдена.");
        }

        var isUsed = await this._opencrmContext.Vehicles.AnyAsync(vehicle =>
            !vehicle.IsDeleted &&
            vehicle.ModelId == modelId);
        if (isUsed)
        {
            return AdminOperationResult.Invalid("Нельзя удалить модель, которая используется в технике.");
        }

        model.IsDeleted = true;
        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success("Модель удалена.");
    }

    /// <summary> Нормализует название. </summary>
    private static string? NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary> Возвращает текстовое описание вида техники. </summary>
    private static string GetKindText(VehicleKind kind)
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
