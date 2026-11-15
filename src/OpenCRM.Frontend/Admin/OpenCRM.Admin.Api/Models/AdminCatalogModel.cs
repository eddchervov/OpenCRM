namespace OpenCRM.Admin.Api.Models;

public sealed class AdminCatalogModel
{
    public int Id { get; init; }
    public int BrandId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int VehiclesCount { get; init; }
}
