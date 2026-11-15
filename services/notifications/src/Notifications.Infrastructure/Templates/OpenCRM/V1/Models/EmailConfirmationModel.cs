using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные письма о подтверждении электронной почты.</summary>
public sealed class EmailConfirmationModel
{
    /// <summary>Имя получателя.</summary>
    [Required, StringLength(254)]
    public required string Name { get; init; }

    /// <summary>Полная ссылка подтверждения, сформированная приложением.</summary>
    [Required, StringLength(4096)]
    public required string ConfirmationUrl { get; init; }
}
