using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные письма для восстановления доступа.</summary>
public sealed class PasswordResetModel
{
    /// <summary>Имя получателя.</summary>
    [Required, StringLength(254)]
    public required string Name { get; init; }

    /// <summary>Полная ссылка сброса пароля, сформированная приложением.</summary>
    [Required, StringLength(4096)]
    public required string ResetUrl { get; init; }
}
