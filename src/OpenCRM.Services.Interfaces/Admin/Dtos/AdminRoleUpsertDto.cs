using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO создания или обновления роли. </summary>
public sealed class AdminRoleUpsertDto
{
    /// <summary> Название роли. </summary>
    [Required]
    [StringLength(256)]
    public string Name { get; init; } = string.Empty;
}

