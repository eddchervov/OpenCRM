using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат получения тестовой техники. </summary>
public sealed class AdminTestDataVehiclesResultDto
{
    /// <summary> Признак того, что данные не найдены. </summary>
    public bool NotFound { get; init; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Список тестовой техники. </summary>
    public IReadOnlyList<AdminTestDataVehicleDto> Vehicles { get; init; } = [];
}

