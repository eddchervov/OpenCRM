using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат поиска моделей справочника. </summary>
public sealed class AdminCatalogModelsResultDto
{
    /// <summary> Признак отсутствия бренда. </summary>
    public bool NotFound { get; init; }

    /// <summary> Текст ошибки. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Список моделей. </summary>
    public IReadOnlyList<AdminCatalogModelDto> Models { get; init; } = [];
}

