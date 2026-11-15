namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Вариант значения для фильтра планировщика. </summary>
public sealed class OrganizationSchedulerOptionDto
{
    /// <summary> Значение варианта. </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary> Отображаемый текст варианта. </summary>
    public string Label { get; init; } = string.Empty;
}
