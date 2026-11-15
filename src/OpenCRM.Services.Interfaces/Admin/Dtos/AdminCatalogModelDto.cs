using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO модели справочника техники в админ-панели. </summary>
public sealed class AdminCatalogModelDto
{
    /// <summary> Идентификатор модели. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор бренда. </summary>
    public int BrandId { get; init; }

    /// <summary> Название модели. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; init; }

    /// <summary> Текстовое название вида техники. </summary>
    public string KindText { get; init; } = string.Empty;

    /// <summary> Количество техники модели. </summary>
    public int VehiclesCount { get; init; }
}

