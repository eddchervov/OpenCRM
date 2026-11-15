namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Вариант бренда техники для фильтра. </summary>
public class VehicleBrandFilterOptionDto
{
    /// <summary> Идентификатор бренда. </summary>
    public required string Id { get; set; }

    /// <summary> Название бренда. </summary>
    public required string Name { get; set; }
}
