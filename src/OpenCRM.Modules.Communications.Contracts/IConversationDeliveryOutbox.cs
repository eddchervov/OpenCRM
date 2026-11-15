using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Выдаёт сообщения Telegram на доставку и принимает подтверждённый результат транспорта. </summary>
public interface IConversationDeliveryOutbox
{
    Task<ConversationDeliveryLeaseDto?> ClaimByMessageIdAsync(
        Guid messageId,
        Guid requestId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid messageId,
        Guid leaseId,
        ConversationDeliveryResultDto result,
        CancellationToken cancellationToken = default);
}
