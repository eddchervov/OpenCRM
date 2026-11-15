using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные нового обращения в поддержку.</summary>
public sealed class SupportMessageModel
{
    /// <summary> Идентификатор сообщения обращения в поддержку. </summary>
    [Required, StringLength(100)]
    public string MessageId { get; init; } = string.Empty;

    /// <summary> Название типа обращения. </summary>
    [Required, StringLength(100)]
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Имя отправителя обращения, может отсутствовать. </summary>
    [StringLength(200)]
    public string? Name { get; init; }

    /// <summary> Электронная почта отправителя, может отсутствовать. </summary>
    [StringLength(254)]
    public string? Email { get; init; }

    /// <summary> Телефон отправителя, может отсутствовать. </summary>
    [StringLength(100)]
    public string? Phone { get; init; }

    /// <summary> Тема обращения в поддержку. </summary>
    [Required, StringLength(250)]
    public string Topic { get; init; } = string.Empty;

    /// <summary> Текст обращения. </summary>
    [Required, StringLength(10000)]
    public string Text { get; init; } = string.Empty;
}
