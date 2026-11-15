namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат наполнения каталога техники. </summary>
public sealed class AdminVehicleCatalogSeedResultDto
{
    /// <summary> Количество созданных брендов. </summary>
    public int CreatedBrands { get; init; }

    /// <summary> Количество созданных моделей. </summary>
    public int CreatedModels { get; init; }

    /// <summary> Количество пропущенных удалённых брендов. </summary>
    public int SkippedDeletedBrands { get; init; }

    /// <summary> Количество пропущенных удалённых моделей. </summary>
    public int SkippedDeletedModels { get; init; }
}
