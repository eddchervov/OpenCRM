namespace OpenCRM.WebApp.Models.Organizations;

/// <summary>
/// Источник содержимого справки рабочего места.
/// Базовая поставка платформы не содержит статей справки: их подключает
/// владелец установки, заменив реализацию в контейнере зависимостей.
/// </summary>
public interface IOrganizationWikiContentProvider
{
    /// <summary> Возвращает <see langword="true"/>, если источник содержит статьи. </summary>
    bool HasContent { get; }

    /// <summary> Возвращает статьи справки, отфильтрованные по поисковому запросу. </summary>
    IReadOnlyList<OrganizationWikiArticleViewModel> Search(string? query);

    /// <summary> Возвращает статью по её адресу или <c>null</c>, если статьи нет. </summary>
    OrganizationWikiArticleViewModel? GetBySlug(string? slug);

    /// <summary> Возвращает статью, открываемую по умолчанию, или <c>null</c>, если статей нет. </summary>
    OrganizationWikiArticleViewModel? GetDefaultArticle();
}
