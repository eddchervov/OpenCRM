using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Управляет диалогами организации и принимает нормализованные обновления Telegram. </summary>
public interface IOrganizationConversationService
{
    Task<ConversationInboxPageDto> GetInboxAsync(
        string userId,
        Guid organizationId,
        ConversationInboxQueryDto query,
        CancellationToken cancellationToken = default);
    Task<ConversationClientInboxPageDto> GetClientInboxAsync(
        string userId,
        Guid organizationId,
        ConversationInboxQueryDto query,
        CancellationToken cancellationToken = default);

    Task<ConversationUnreadSummaryDto> GetUnreadSummaryAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<ConversationDeliveryHealthDto> GetDeliveryHealthForOrganizationAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<ConversationMessagesPageDto?> GetMessagesAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        long? beforeSequence = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<ConversationMessagesPageDto?> GetForClientAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    Task<ConversationClientRoutesDto?> GetClientRoutesAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    Task<ConversationMessageDto> SendAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        SendConversationMessageDto request,
        CancellationToken cancellationToken = default);

    Task<bool> MarkReadAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        long throughSequence,
        CancellationToken cancellationToken = default);

    Task<bool> LinkClientAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        Guid? clientId,
        CancellationToken cancellationToken = default);

    Task<bool> IngestTelegramMessageAsync(
        IngestTelegramMessageDto update,
        CancellationToken cancellationToken = default);

    Task SetTelegramChatActiveAsync(
        Guid organizationId,
        long botId,
        long chatId,
        bool isActive,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);

    Task<bool> BindTelegramClientAsync(
        Guid organizationId,
        long botId,
        long chatId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);
}
