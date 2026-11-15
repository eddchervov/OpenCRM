namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>
/// Пункт выбора связанной сущности для ручной задачи планировщика.
/// </summary>
public sealed class OrganizationSchedulerLinkedItemOptionDto
{
    /// <summary>Идентификатор неисправности или обслуживания.</summary>
    public Guid Id { get; init; }

    /// <summary>Текст, который видит пользователь в списке выбора.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Идентификатор техники, к которой относится связанная сущность.</summary>
    public Guid VehicleId { get; init; }

    /// <summary>Название техники для дополнительного контекста.</summary>
    public string VehicleName { get; init; } = string.Empty;
}
