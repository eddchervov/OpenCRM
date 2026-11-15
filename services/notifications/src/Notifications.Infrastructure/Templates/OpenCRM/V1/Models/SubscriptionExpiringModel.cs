using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные напоминания о завершении подписки.</summary>
public sealed class SubscriptionExpiringModel
{
    /// <summary>Имя получателя.</summary>
    [Required, StringLength(200)]
    public required string Name { get; init; }

    /// <summary>Название подписки.</summary>
    [Required, StringLength(200)]
    public required string SubscriptionName { get; init; }

    /// <summary>Дата окончания подписки в календаре пользователя.</summary>
    public required DateOnly EndDate { get; init; }

    /// <summary>Полная ссылка на управление подпиской.</summary>
    [Required, StringLength(4096)]
    public required string RenewalUrl { get; init; }
}
