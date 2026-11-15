namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Быстрое действие рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceQuickActionDto
{
    /// <summary> Код действия. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Заголовок действия. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Шаблон адреса действия. </summary>
    public string UrlTemplate { get; init; } = string.Empty;

    /// <summary> CSS-класс иконки действия. </summary>
    public string IconCssClass { get; init; } = string.Empty;

    /// <summary> Код формы, если есть. </summary>
    public string? FormCode { get; init; }

    /// <summary> Код требуемой функции, если есть. </summary>
    public string? RequiredFeatureCode { get; init; }

    /// <summary> Признак основного действия. </summary>
    public bool IsPrimary { get; init; }

    /// <summary> Признак видимости действия. </summary>
    public bool IsVisible { get; init; } = true;
}

