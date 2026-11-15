using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Обслуживает каналы организации на доверенной стороне транспорта. </summary>
public interface IOrganizationChannelService
{
    Task<Guid> EnsureConnectionAsync(
        EnsureOrganizationChannelConnectionDto request,
        CancellationToken cancellationToken = default);

    Task<bool> IngestMessageAsync(
        ChannelMessageReceivedDto message,
        CancellationToken cancellationToken = default);

    Task<bool> SetChannelActiveAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        bool isActive,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);

    Task<bool> BindClientAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        string externalUserId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary> Отзывает подтверждённые подключения клиента и закрывает их диалоги для отправки. </summary>
    Task RevokeClientAsync(
        Guid organizationId,
        Guid clientId,
        string providerCode,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);
}
