using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные для ссылок сброса пароля в приложении и браузере.</summary>
public sealed class MobilePasswordResetModel
{
    /// <summary> Идентификатор пользователя для сброса пароля. </summary>
    [Required, StringLength(450)]
    public string UserId { get; init; } = string.Empty;

    /// <summary> Код подтверждения сброса пароля. </summary>
    [Required, StringLength(4096)]
    public string Code { get; init; } = string.Empty;

    /// <summary> Ссылка для сброса пароля. </summary>
    [Required, StringLength(4096)]
    public string ResetUrl { get; init; } = string.Empty;
}
