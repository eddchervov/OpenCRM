namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Пункт меню рабочего пространства организации. </summary>
public sealed record OrganizationWorkspaceMenuItemDto
{
    /// <summary> Признак доступности раздела в текущем тарифе. </summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary> Причина тарифного ограничения видимого пункта. </summary>
    public string? UnavailableMessage { get; init; }

    /// <summary> Код пункта меню. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Коды разделов, к которым относится пункт. </summary>
    public IReadOnlyList<string> SectionCodes { get; init; } = [];

    /// <summary> Заголовок пункта меню. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Шаблон адреса пункта меню. </summary>
    public string UrlTemplate { get; init; } = string.Empty;

    /// <summary> CSS-класс иконки пункта меню. </summary>
    public string IconCssClass { get; init; } = string.Empty;

    /// <summary> Код требуемой функции, если есть. </summary>
    public string? RequiredFeatureCode { get; init; }

    /// <summary> Заголовок группы меню. </summary>
    public string GroupTitle { get; init; } = "Работа";

    /// <summary> Подпись раздела, если есть. </summary>
    public string? SectionLabel { get; init; }

    /// <summary> Значение счётчика пункта меню, если есть. </summary>
    public int? Count { get; init; }

    /// <summary> Признак разделителя перед пунктом меню. </summary>
    public bool HasDividerBefore { get; init; }

    /// <summary> Признак видимости пункта меню. </summary>
    public bool IsVisible { get; init; } = true;
}
