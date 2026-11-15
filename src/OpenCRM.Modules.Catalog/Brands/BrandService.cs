using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Brands;
using OpenCRM.Services.Interfaces.Brands;
using OpenCRM.Services.Interfaces.Brands.Dtos;
using System.Linq.Expressions;

namespace OpenCRM.Modules.Catalog.Brands;

/// <summary> Сервис получения справочника брендов техники. </summary>
public class BrandService(IBrandRepository brandRepository) : IBrandService
{
    /// <summary> Зависимость репозитория брендов. </summary>
    private readonly IBrandRepository _brandRepository = brandRepository;

    /// <summary> Возвращает список брендов. </summary>
    public async Task<IEnumerable<BrandOptionDto>> GetOptions()
    {
        return await _brandRepository.Get(MapToDtoExpr).ToListAsync();
    }

    /// <summary> Возвращает список брендов с фильтром по виду техники. </summary>
    public async Task<IEnumerable<BrandOptionDto>> GetOptions(OpenCRM.Domain.Enums.VehicleKind? kind)
    {
        if (!kind.HasValue)
        {
            return await GetOptions();
        }

        return await _brandRepository.Get(new OpenCRM.Domain.Interfaces.Brands.BrandFilter
        {
            Kind = kind
        }, MapToDtoExpr).ToListAsync();
    }

    /// <summary> Выражение проекции бренда в DTO. </summary>
    private readonly static Expression<Func<Brand, BrandOptionDto>> MapToDtoExpr = brand => new BrandOptionDto(brand.Id, brand.Name, brand.Kind);
}
