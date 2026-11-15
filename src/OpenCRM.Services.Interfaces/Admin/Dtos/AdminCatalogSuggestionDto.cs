using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO предложения пользователя по пополнению справочника. </summary>
public sealed class AdminCatalogSuggestionDto
{
    /// <summary> Идентификатор предложения. </summary>
    public int Id { get; init; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; init; }

    /// <summary> Текстовое название вида техники. </summary>
    public string KindText { get; init; } = string.Empty;

    /// <summary> Название бренда. </summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary> Название модели. </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary> Статус предложения. </summary>
    public VehicleCatalogSuggestionStatus Status { get; init; }

    /// <summary> Текстовое название статуса. </summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary> Идентификатор пользователя, предложившего запись. </summary>
    public string SuggestedByUserId { get; init; } = string.Empty;

    /// <summary> Email предложившего пользователя. </summary>
    public string? SuggestedByEmail { get; init; }

    /// <summary> Время создания предложения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время рассмотрения. </summary>
    public DateTime? ReviewedAt { get; init; }

    /// <summary> Идентификатор администратора, рассмотревшего предложение. </summary>
    public string? ReviewedByUserId { get; init; }

    /// <summary> Комментарий администратора. </summary>
    public string? AdminComment { get; init; }

    /// <summary> Идентификатор одобренного бренда. </summary>
    public int? ApprovedBrandId { get; init; }

    /// <summary> Идентификатор одобренной модели. </summary>
    public int? ApprovedModelId { get; init; }
}

