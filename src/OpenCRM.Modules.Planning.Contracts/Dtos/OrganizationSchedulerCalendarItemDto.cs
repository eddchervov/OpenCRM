namespace OpenCRM.Modules.Planning.Contracts.Dtos;

/// <summary> Элемент календаря планировщика организации. </summary>
public sealed class OrganizationSchedulerCalendarItemDto
{
    /// <summary> Идентификатор элемента календаря. </summary>
    public Guid Id { get; init; }

    /// <summary> Код вида элемента календаря. </summary>
    public string ItemKind { get; init; } = "maintenance";

    /// <summary> Идентификатор транспортного средства, если есть. </summary>
    public Guid? VehicleId { get; init; }

    /// <summary> Заголовок элемента. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Подзаголовок элемента, если есть. </summary>
    public string? Subtitle { get; init; }

    /// <summary> Название типа элемента. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Название статуса элемента. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> CSS-класс бейджа статуса. </summary>
    public string StatusBadgeClass { get; init; } = "text-bg-light";

    /// <summary> Дата элемента календаря. </summary>
    public DateTime Date { get; init; }

    /// <summary> Плановое время начала в строковом виде. </summary>
    public string PlannedStartTime { get; init; } = "09:00";

    /// <summary> Плановая дата окончания, если есть. </summary>
    public DateTime? PlannedEndDate { get; init; }

    /// <summary> Плановое время окончания в строковом виде. </summary>
    public string PlannedEndTime { get; init; } = "10:00";

    /// <summary> Комментарий к элементу, если есть. </summary>
    public string? Comment { get; init; }

    /// <summary> URL страницы с деталями элемента. </summary>
    public string DetailUrl { get; init; } = string.Empty;
}
