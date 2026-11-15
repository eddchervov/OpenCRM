using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO предложения справочника для дашборда. </summary>
public sealed class AdminDashboardCatalogSuggestionDto
{
    /// <summary> Идентификатор предложения. </summary>
    public int Id { get; init; }

    /// <summary> Название бренда. </summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary> Название модели. </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary> Статус предложения. </summary>
    public VehicleCatalogSuggestionStatus Status { get; init; }

    /// <summary> Время создания предложения. </summary>
    public DateTime CreatedAt { get; init; }
}
