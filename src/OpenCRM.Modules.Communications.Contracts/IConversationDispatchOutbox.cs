using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Публикует wakeup-события из transactional outbox в брокер. </summary>
public interface IConversationDispatchOutbox
{
    Task<IReadOnlyList<ConversationDispatchLeaseDto>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> CompletePublicationAsync(
        Guid dispatchId,
        Guid leaseId,
        bool brokerAccepted,
        string? errorCode = null,
        CancellationToken cancellationToken = default);

    Task<ConversationDeliveryHealthDto> GetDeliveryHealthAsync(CancellationToken cancellationToken = default);
}
