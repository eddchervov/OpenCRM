using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO бренда справочника техники в админ-панели. </summary>
public sealed class AdminCatalogBrandDto
{
    /// <summary> Идентификатор бренда. </summary>
    public int Id { get; init; }

    /// <summary> Название бренда. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Ключ локального логотипа марки. </summary>
    public string? LogoKey { get; init; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; init; }

    /// <summary> Текстовое название вида техники. </summary>
    public string KindText { get; init; } = string.Empty;

    /// <summary> Количество моделей бренда. </summary>
    public int ModelsCount { get; init; }

    /// <summary> Количество техники бренда. </summary>
    public int VehiclesCount { get; init; }
}

