namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Вариант техники для выбора в списках. </summary>
public sealed class VehicleOptionDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Отображаемое название техники. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительное описание техники. </summary>
    public string? Subtitle { get; init; }

    /// <summary> Текущий пробег, если известен. </summary>
    public int? CurrentMileage { get; init; }

    /// <summary> Единица измерения пробега. </summary>
    public string MileageUnit { get; init; } = string.Empty;

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; init; }
}
