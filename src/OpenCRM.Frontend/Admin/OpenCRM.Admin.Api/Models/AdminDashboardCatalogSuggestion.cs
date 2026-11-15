namespace OpenCRM.Admin.Api.Models;

/// <summary> Предложение по каталогу для административной панели. </summary>
public sealed class AdminDashboardCatalogSuggestion
{
    /// <summary> Идентификатор предложения. </summary>
    public int Id { get; init; }

    /// <summary> Название марки. </summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary> Название модели. </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary> Статус предложения. </summary>
    public int Status { get; init; }

    /// <summary> Момент создания предложения. </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
