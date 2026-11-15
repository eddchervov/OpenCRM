using OpenCRM.Services.Interfaces.Brands.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Brands;

/// <summary> Доступ к списку марок техники. </summary>
public interface IBrandService
{
    /// <summary> Возвращает варианты марок техники. </summary>
    Task<IEnumerable<BrandOptionDto>> GetOptions();

    /// <summary> Возвращает варианты марок техники по виду техники. </summary>
    Task<IEnumerable<BrandOptionDto>> GetOptions(OpenCRM.Domain.Enums.VehicleKind? kind);
}

