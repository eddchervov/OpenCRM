using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Services.Interfaces.Home;

/// <summary> Контракт сервиса работы с обращениями в поддержку. </summary>
public interface ISupportMessageService
{
    /// <summary> Создаёт обращение в поддержку. </summary>
    Task<SupportMessageCreateResultDto> CreateAsync(SupportMessageCreateDto dto);

    /// <summary>Возвращает обращения пользователя и переписку выбранного обращения.</summary>
    Task<SupportInboxDto> GetInboxAsync(
        string userId,
        int? selectedId,
        CancellationToken cancellationToken = default);

    /// <summary>Добавляет ответ пользователя и возвращает закрытое обращение в работу.</summary>
    Task<SupportMessageReplyResult> ReplyAsync(
        string userId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary> Добавляет ответ пользователя вместе с вложениями. </summary>
    Task<SupportMessageReplyResult> ReplyAsync(
        string userId,
        int messageId,
        string text,
        CancellationToken cancellationToken,
        IReadOnlyList<SupportAttachmentUploadDto> attachments);

    /// <summary>Проверяет доступ пользователя к организации для создания обращения.</summary>
    Task<bool> HasOrganizationAccessAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
