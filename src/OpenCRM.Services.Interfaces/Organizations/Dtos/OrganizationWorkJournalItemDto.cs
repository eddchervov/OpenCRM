namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Запись журнала событий работы организации. </summary>
public sealed class OrganizationWorkJournalItemDto
{
    /// <summary> Время события. </summary>
    public DateTime OccurredAt { get; init; }

    /// <summary> Код вида события. </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary> Код события. </summary>
    public string EventCode { get; init; } = string.Empty;

    /// <summary> Название вида события. </summary>
    public string KindName { get; init; } = string.Empty;

    /// <summary> Заголовок записи. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Описание записи, если есть. </summary>
    public string? Description { get; init; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительная подпись транспортного средства. </summary>
    public string? VehicleSubtitle { get; init; }

    /// <summary> Имя пользователя, выполнившего событие, если известно. </summary>
    public string? ActorDisplayName { get; init; }

    /// <summary> Дополнительный текст записи. </summary>
    public string? MetaText { get; init; }

    /// <summary> Адрес страницы с деталями, если есть. </summary>
    public string? DetailUrl { get; init; }

    /// <summary> Адрес страницы транспортного средства, если есть. </summary>
    public string? VehicleUrl { get; init; }
}
