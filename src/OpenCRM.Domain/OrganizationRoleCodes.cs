namespace OpenCRM.Domain;

/// <summary> Коды ролей в организации. </summary>
public static class OrganizationRoleCodes
{
    /// <summary> Код роли владельца организации. </summary>
    public const string Owner = "owner";

    /// <summary> Код роли администратора организации. </summary>
    public const string Admin = "admin";

    /// <summary> Код роли сотрудника организации. </summary>
    public const string Employee = "employee";

    /// <summary> Код роли исполнителя работ. </summary>
    public const string Performer = "performer";

    /// <summary> Код роли наблюдателя с доступом только для чтения. </summary>
    public const string Viewer = "viewer";

    /// <summary> Полный список кодов ролей организации. </summary>
    public static readonly IReadOnlyCollection<string> All = [Owner, Admin, Employee, Performer, Viewer];

    /// <summary> Коды ролей, входящих в управление организацией. </summary>
    public static readonly IReadOnlyCollection<string> ManageRoles = [Owner, Admin];
}
