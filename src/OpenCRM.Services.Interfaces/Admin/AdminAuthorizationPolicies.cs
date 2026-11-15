namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Класс с именами политик авторизации администратора. </summary>
public static class AdminAuthorizationPolicies
{
    /// <summary> Имя политики доступа к административной панели. </summary>
    public const string Access = "Admin.Access";

    /// <summary> Имя политики просмотра дашборда. </summary>
    public const string ViewDashboard = "Admin.Dashboard.View";

    /// <summary> Имя политики просмотра пользователей. </summary>
    public const string ViewUsers = "Admin.Users.View";

    /// <summary> Имя политики управления пользователями. </summary>
    public const string ManageUsers = "Admin.Users.Manage";

    /// <summary> Имя политики входа от имени пользователей. </summary>
    public const string ImpersonateUsers = "Admin.Users.Impersonate";

    /// <summary> Имя политики просмотра биллинга. </summary>
    public const string ViewBilling = "Admin.Billing.View";

    /// <summary> Имя политики управления биллингом. </summary>
    public const string ManageBilling = "Admin.Billing.Manage";

    /// <summary> Имя политики просмотра контента. </summary>
    public const string ViewContent = "Admin.Content.View";

    /// <summary> Имя политики управления контентом. </summary>
    public const string ManageContent = "Admin.Content.Manage";

    /// <summary> Имя политики просмотра коммуникаций. </summary>
    public const string ViewCommunications = "Admin.Communications.View";

    /// <summary> Имя политики управления коммуникациями. </summary>
    public const string ManageCommunications = "Admin.Communications.Manage";

    /// <summary> Имя политики управления конфигурацией. </summary>
    public const string ManageConfiguration = "Admin.Configuration.Manage";

    /// <summary> Имя политики управления разрешениями. </summary>
    public const string ManagePermissions = "Admin.Permissions.Manage";

    /// <summary> Список всех политик авторизации. </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Access,
        ViewDashboard,
        ViewUsers,
        ManageUsers,
        ImpersonateUsers,
        ViewBilling,
        ManageBilling,
        ViewContent,
        ManageContent,
        ViewCommunications,
        ManageCommunications,
        ManageConfiguration
    ];
}
