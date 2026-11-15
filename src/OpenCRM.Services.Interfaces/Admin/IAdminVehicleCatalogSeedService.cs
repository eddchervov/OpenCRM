using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Наполнение справочника техники. </summary>
public interface IAdminVehicleCatalogSeedService
{
    /// <summary> Наполняет справочник техники данными. </summary>
    Task<AdminVehicleCatalogSeedResultDto> SeedAsync(CancellationToken cancellationToken = default);
}
