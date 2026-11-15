using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление справочником марок и моделей техники. </summary>
public interface IAdminVehicleCatalogService
{
    /// <summary> Возвращает марки техники с фильтрами по виду и поиску. </summary>
    Task<IReadOnlyList<AdminCatalogBrandDto>> GetBrandsAsync(VehicleKind? kind, string? search);

    /// <summary> Создаёт марку техники. </summary>
    Task<AdminOperationResult> CreateBrandAsync(AdminSaveBrandDto dto);

    /// <summary> Обновляет марку техники. </summary>
    Task<AdminOperationResult> UpdateBrandAsync(int brandId, AdminSaveBrandDto dto);

    /// <summary> Удаляет марку техники. </summary>
    Task<AdminOperationResult> DeleteBrandAsync(int brandId);

    /// <summary> Возвращает модели марки техники с поиском. </summary>
    Task<AdminCatalogModelsResultDto> GetModelsAsync(int brandId, string? search);

    /// <summary> Создаёт модель техники у марки. </summary>
    Task<AdminOperationResult> CreateModelAsync(int brandId, AdminSaveModelDto dto);

    /// <summary> Обновляет модель техники. </summary>
    Task<AdminOperationResult> UpdateModelAsync(int modelId, AdminSaveModelDto dto);

    /// <summary> Удаляет модель техники. </summary>
    Task<AdminOperationResult> DeleteModelAsync(int modelId);
}
