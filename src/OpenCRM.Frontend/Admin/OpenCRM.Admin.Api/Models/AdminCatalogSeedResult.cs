namespace OpenCRM.Admin.Api.Models;

public sealed class AdminCatalogSeedResult
{
    public int CreatedBrands { get; init; }
    public int CreatedModels { get; init; }
    public int SkippedDeletedBrands { get; init; }
    public int SkippedDeletedModels { get; init; }
}
