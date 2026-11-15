using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Сохраняет вложения и проверяет доступ к их содержимому перед чтением. </summary>
public interface IConversationAttachmentService
{
    Task<ConversationAttachmentDto> UploadAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<ConversationAttachmentContentDto?> GetAsync(
        string userId,
        Guid organizationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    Task<ConversationAttachmentContentDto?> GetForDeliveryAsync(
        Guid messageId,
        Guid leaseId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);
}
