namespace OpenCRM.WebApp.Dtos.Admin.Users;

/// <summary> Запрос назначения подписки пользователю. </summary>
public class AssignUserSubscriptionRequest
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int PlanId { get; set; }
}

