namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>Данные для оперативной карточки работы детейлинга.</summary>
public sealed class OrganizationWorkdayItemDto
{
    /// <summary> Идентификатор карточки работы. </summary>
    public Guid Id { get; init; }

    /// <summary> Название транспортного средства. </summary>
    public string VehicleName { get; init; } = string.Empty;

    /// <summary> Ключ логотипа связанной марки из справочника. </summary>
    public string? VehicleBrandLogoKey { get; init; }

    /// <summary> Регистрационный знак, если есть. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> Имя клиента, если есть. </summary>
    public string? ClientName { get; init; }

    /// <summary> Телефон клиента, если есть. </summary>
    public string? ClientPhone { get; init; }

    /// <summary> Список услуг по работе. </summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary> Общее количество работ по карточке. </summary>
    public int WorkTotalCount { get; init; }

    /// <summary> Есть назначение исполнителя или сохранённые операции работ. </summary>
    public bool HasTrackedWork { get; init; }

    /// <summary> Количество завершённых работ. </summary>
    public int WorkCompletedCount { get; init; }

    /// <summary> Количество приостановленных работ. </summary>
    public int WorkPausedCount { get; init; }

    /// <summary> Дата работы. </summary>
    public DateTime Date { get; init; }

    /// <summary> Время готовности к передаче, если есть. </summary>
    public DateTime? ReadyForHandoverAt { get; init; }

    /// <summary> Время передачи, если есть. </summary>
    public DateTime? HandedOverAt { get; init; }

    /// <summary> Время начала работы, если есть. </summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary> Дата окончания работы, если есть. </summary>
    public DateTime? EndDate { get; init; }

    /// <summary> Время окончания работы, если есть. </summary>
    public TimeSpan? EndTime { get; init; }

    /// <summary> Место выполнения работы, если есть. </summary>
    public string? Location { get; init; }

    /// <summary> Исполнитель работы, если есть. </summary>
    public string? Performer { get; init; }
}
