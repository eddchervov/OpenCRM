using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO сохранения бренда для админ-панели. </summary>
public sealed class AdminSaveBrandDto
{
    /// <summary> Название бренда. </summary>
    public string? Name { get; init; }

    /// <summary> Ключ локального логотипа: null сохраняет прежний ключ, пустая строка очищает его. </summary>
    public string? LogoKey { get; init; }

    /// <summary> Вид техники бренда. </summary>
    public VehicleKind Kind { get; init; }
}

