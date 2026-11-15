namespace OpenCRM.WebApp.Security;

/// <summary> Хранит имена административных ролей и их наборов. </summary>
public static class AdminRoleNames
{
    /// <summary> Имя системной роли. </summary>
    public const string System = "System";

    /// <summary> Имя роли администратора. </summary>
    public const string Admin = "Admin";

    /// <summary> Имя роли модератора. </summary>
    public const string Moderator = "Moderator";

    /// <summary> Набор ролей с правами просмотра. </summary>
    public const string ViewerRoles = System + "," + Admin + "," + Moderator;

    /// <summary> Набор ролей с правами управления. </summary>
    public const string ManagerRoles = System + "," + Admin;

    /// <summary> Набор, ограниченный системной ролью. </summary>
    public const string SystemOnly = System;
}
