using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO списка ролей для админ-панели. </summary>
public sealed class AdminRolesDto
{
    /// <summary> Список ролей. </summary>
    public IReadOnlyList<AdminRoleDto> Roles { get; init; } = [];

    /// <summary> Общее количество ролей. </summary>
    public int TotalRoles { get; init; }

    /// <summary> Количество защищённых ролей. </summary>
    public int ProtectedRolesCount { get; init; }

    /// <summary> Количество ролей с пользователями. </summary>
    public int RolesWithUsersCount { get; init; }

    /// <summary> Общее количество назначений ролей. </summary>
    public int TotalAssignmentsCount { get; init; }
}

