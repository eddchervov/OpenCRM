namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos;

/// <summary>
/// Группа тарифных планов
/// </summary>
public class SubscriptionPlanGroupDto
{
    /// <summary>
    /// Название группы
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Тарифные планы в группе
    /// </summary>
    public IEnumerable<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();

    /// <summary>
    /// Порядок отображения группы
    /// </summary>
    public int Order { get; set; }
}

