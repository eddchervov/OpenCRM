namespace Notifications.Infrastructure.Templating;

/// <summary>Оформление писем и разрешённые системные шаблоны приложения.</summary>
public sealed class SystemTemplateApplicationOptions
{
    /// <summary>Название приложения в теме и общем макете письма.</summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary>Короткая текстовая эмблема в шапке письма.</summary>
    public string? BrandMark { get; init; }

    /// <summary>Список разрешённых ключей системных шаблонов.</summary>
    public string[] Templates { get; init; } = [];
}
