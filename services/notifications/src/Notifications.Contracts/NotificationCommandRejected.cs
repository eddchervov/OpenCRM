namespace Notifications.Contracts;

/// <summary>Сообщает об отклонении некорректной команды без публикации её содержимого.</summary>
/// <param name="EventId">Идентификатор события.</param>
/// <param name="ApplicationId">Приложение, отправившее команду.</param>
/// <param name="MessageId">Идентификатор исходного сообщения MassTransit.</param>
/// <param name="Command">Тип отклонённой команды.</param>
/// <param name="ErrorCode">Код причины отклонения.</param>
public sealed record NotificationCommandRejected(
    Guid EventId,
    string ApplicationId,
    Guid? MessageId,
    string Command,
    string ErrorCode);
