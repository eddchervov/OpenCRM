using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO изменения участника роли. </summary>
public sealed class AdminRoleMemberUpdateDto
{
    /// <summary> Идентификатор пользователя. </summary>
    [Required]
    public string UserId { get; init; } = string.Empty;
}

