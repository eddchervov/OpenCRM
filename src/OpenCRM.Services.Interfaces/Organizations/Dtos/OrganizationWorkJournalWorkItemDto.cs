namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Запись журнала выполненных работ организации. </summary>
public sealed class OrganizationWorkJournalWorkItemDto
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

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительная подпись транспортного средства. </summary>
    public string? VehicleSubtitle { get; init; }

    /// <summary> Идентификатор клиента, если есть. </summary>
    public Guid? ClientId { get; init; }

    /// <summary> Имя клиента, если есть. </summary>
    public string? ClientName { get; init; }

    /// <summary> Текст результата работы, если есть. </summary>
    public string? ResultText { get; init; }

    /// <summary> Дополнительный текст записи. </summary>
    public string? MetaText { get; init; }

    /// <summary> Стоимость работы, если задана. </summary>
    public decimal? Price { get; init; }

    /// <summary> Адрес страницы с деталями, если есть. </summary>
    public string? DetailUrl { get; init; }

    /// <summary> Адрес страницы транспортного средства, если есть. </summary>
    public string? VehicleUrl { get; init; }
}
