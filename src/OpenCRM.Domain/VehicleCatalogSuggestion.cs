using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

/// <summary> Предложение пользователя о добавлении техники в общий каталог. </summary>
public class VehicleCatalogSuggestion
{
    /// <summary> Идентификатор предложения. </summary>
    public int Id { get; set; }

    /// <summary> Тип техники. </summary>
    public VehicleKind Kind { get; set; }

    /// <summary> Предложенный бренд. </summary>
    public string BrandName { get; set; } = default!;

    /// <summary> Предложенная модель. </summary>
    public string ModelName { get; set; } = default!;

    /// <summary> Идентификатор пользователя, создавшего предложение. </summary>
    public string SuggestedByUserId { get; set; } = default!;

    /// <summary> Дата создания предложения. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Текущий статус предложения. </summary>
    public VehicleCatalogSuggestionStatus Status { get; set; }

    /// <summary> Идентификатор утвержденного бренда. </summary>
    public int? ApprovedBrandId { get; set; }

    /// <summary> Идентификатор утвержденной модели. </summary>
    public int? ApprovedModelId { get; set; }

    /// <summary> Идентификатор администратора, обработавшего предложение. </summary>
    public string? ReviewedByUserId { get; set; }

    /// <summary> Дата модерации предложения. </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary> Комментарий модератора. </summary>
    public string? AdminComment { get; set; }
}
