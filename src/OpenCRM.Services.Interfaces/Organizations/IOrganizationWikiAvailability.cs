namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Сообщает, подключено ли содержимое wiki рабочего пространства. </summary>
public interface IOrganizationWikiAvailability
{
    /// <summary> Возвращает <see langword="true"/>, если подключена хотя бы одна статья. </summary>
    bool HasContent { get; }
}
