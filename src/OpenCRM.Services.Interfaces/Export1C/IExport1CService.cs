using OpenCRM.Services.Interfaces.Export1C.Dtos;

namespace OpenCRM.Services.Interfaces.Export1C;

/// <summary> Контракт сервиса выгрузки данных в 1С. </summary>
public interface IExport1CService
{
    /// <summary> Создаёт пакет данных для выгрузки в 1С по организации с учётом настроек. </summary>
    Task<Export1CPackageDto> CreateAsync(Guid organizationId, Export1COptionsDto options);
}
