namespace OpenCRM.WebApp.Models.Organizations;

/// <summary>
/// Пустой источник справки: базовая поставка работает без встроенных статей.
/// Раздел справки в рабочем месте при этом не отображается, а API возвращает
/// пустой список без ошибок.
/// </summary>
public sealed class EmptyOrganizationWikiContentProvider : IOrganizationWikiContentProvider
{
    /// <inheritdoc />
    public bool HasContent => false;

    /// <summary> Единственный пустой список статей. </summary>
    private static readonly IReadOnlyList<OrganizationWikiArticleViewModel> s_empty = [];

    /// <inheritdoc />
    public IReadOnlyList<OrganizationWikiArticleViewModel> Search(string? query) => s_empty;

    /// <inheritdoc />
    public OrganizationWikiArticleViewModel? GetBySlug(string? slug) => null;

    /// <inheritdoc />
    public OrganizationWikiArticleViewModel? GetDefaultArticle() => null;
}
