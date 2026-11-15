namespace OpenCRM.WebApp.Dtos.Admin.Users;

/// <summary> Запрос обновления права пользователя на тестовые подписки. </summary>
public class UpdateUserTestSubscriptionsRequest
{
    /// <summary> Признак разрешения использования тестовых подписок. </summary>
    public bool CanUseTestSubscriptions { get; set; }
}

