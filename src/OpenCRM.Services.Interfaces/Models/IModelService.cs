using OpenCRM.Services.Interfaces.Models.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Models;

/// <summary> Контракт сервиса моделей техники. </summary>
public interface IModelService
{
    /// <summary> Возвращает модели техники для марки. </summary>
    Task<IEnumerable<ModelOptionDto>> GetOptions(int brandId);

    /// <summary> Возвращает модели техники для марки с учётом вида техники. </summary>
    Task<IEnumerable<ModelOptionDto>> GetOptions(int brandId, OpenCRM.Domain.Enums.VehicleKind? kind);
}

