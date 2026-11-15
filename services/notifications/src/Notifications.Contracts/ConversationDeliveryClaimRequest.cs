namespace Notifications.Contracts;

/// <summary> Передаёт идентификатор адресного запроса для захвата сообщения. </summary>
public sealed record ConversationDeliveryClaimRequest(Guid RequestId);
