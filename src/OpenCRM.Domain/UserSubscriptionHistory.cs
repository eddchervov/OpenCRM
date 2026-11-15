using OpenCRM.Domain.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain;

/// <summary> Историческая запись о подписке пользователя. </summary>
public class UserSubscriptionHistory
{
    /// <summary> Идентификатор записи истории. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь, к которому относится запись истории. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план записи истории. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// // Плановая дата окончания на момент создания записи
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Фактическая дата окончания (для expired/replaced)
    /// </summary>
    public DateTime? ActualEndDate { get; set; }

    /// <summary>
    /// created - создание новой подписки
    /// extended - продление подписки (с комментарием о датах)
    /// expired - автоматическое истечение подписки
    /// replaced - замена подписки на новый тариф
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary> Признак автоматического продления подписки. </summary>
    public bool AutoRenewal { get; set; }

    /// <summary> Код источника оформления подписки. </summary>
    public string SourceCode { get; set; } = "unknown";

    /// <summary> Дата и время создания записи истории. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Комментарий к записи истории. </summary>
    public string? Comment { get; set; } = null;
}

