namespace OpenCRM.Domain;

/// <summary> Закрепление тега за клиентом организации. </summary>
public class OrganizationClientTagAssignment
{
    /// <summary> Идентификатор клиента организации. </summary>
    public Guid OrganizationClientId { get; set; }

    /// <summary> Клиент организации, получивший тег. </summary>
    public OrganizationClient OrganizationClient { get; set; } = null!;

    /// <summary> Идентификатор тега клиента. </summary>
    public Guid OrganizationClientTagId { get; set; }

    /// <summary> Закреплённый тег клиента. </summary>
    public OrganizationClientTag OrganizationClientTag { get; set; } = null!;
}
