using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Данные напоминания о платном или демонстрационном периоде.</summary>
public sealed class SubscriptionNoticeModel
{
    /// <summary> Тип напоминания о периоде подписки. </summary>
    [Required, RegularExpression("^(upcoming|paid-expired|demo-expired|demo-available)$")]
    public string Kind { get; init; } = string.Empty;

    /// <summary> Название организации для письма, может отсутствовать. </summary>
    [StringLength(500)]
    public string? OrganizationName { get; init; }

    /// <summary> Название тарифного плана. </summary>
    [Required, StringLength(200)]
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Дата окончания периода подписки. </summary>
    public DateOnly EndDate { get; init; }

    /// <summary> Количество оставшихся дней периода. </summary>
    [Range(0, 36500)]
    public int DaysRemaining { get; init; }

    /// <summary> Длительность периода в днях. </summary>
    [Range(0, 36500)]
    public int DurationDays { get; init; }

    /// <summary> Признак доступности бесплатного рабочего пространства после окончания тарифа. </summary>
    public bool HasFreeWorkspace { get; init; }

    /// <summary> Ссылка на действие из напоминания. </summary>
    [Required, StringLength(4096)]
    public string ActionUrl { get; init; } = string.Empty;
}
