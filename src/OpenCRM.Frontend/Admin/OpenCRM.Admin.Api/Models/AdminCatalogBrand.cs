namespace OpenCRM.Admin.Api.Models;

public sealed class AdminCatalogBrand
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Kind { get; init; }
    public string KindText { get; init; } = string.Empty;
    public int ModelsCount { get; init; }
}
