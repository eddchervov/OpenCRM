using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные письма с одноразовым кодом входа.</summary>
public sealed class LoginCodeModel
{
    /// <summary> Одноразовый код входа. </summary>
    [Required, RegularExpression(@"^\d{8}$")]
    public string Code { get; init; } = string.Empty;
}
