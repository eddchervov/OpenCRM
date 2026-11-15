using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO рассмотрения предложения по каталогу. </summary>
public sealed class AdminReviewCatalogSuggestionDto
{
    /// <summary> Комментарий к рассмотрению. </summary>
    public string? Comment { get; init; }
}

