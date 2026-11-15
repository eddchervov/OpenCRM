using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Утилиты нормализации данных пользователей. </summary>
internal static class AdminUserRules
{
    /// <summary> Формирует название роли в организации. </summary>
    public static string FormatOrganizationRole(string? roleCode) => roleCode switch
    {
        OrganizationRoleCodes.Owner => "Владелец",
        OrganizationRoleCodes.Admin => "Администратор",
        OrganizationRoleCodes.Employee => "Сотрудник",
        OrganizationRoleCodes.Performer => "Исполнитель",
        OrganizationRoleCodes.Viewer => "Наблюдатель",
        _ => roleCode ?? "Роль не указана"
    };
}
