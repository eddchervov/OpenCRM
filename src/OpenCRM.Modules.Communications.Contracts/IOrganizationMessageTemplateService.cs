using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Управляет шаблонами сообщений организации и формирует их проверенный предпросмотр. </summary>
public interface IOrganizationMessageTemplateService
{
    Task<IReadOnlyList<MessageTemplateGroupDto>> GetGroupsAsync(
        string userId,
        Guid organizationId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    Task<MessageTemplateGroupDto> SaveGroupAsync(
        string userId,
        Guid organizationId,
        SaveMessageTemplateGroupDto request,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveGroupAsync(
        string userId,
        Guid organizationId,
        Guid groupId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<MessageTemplateDto> SaveTemplateAsync(
        string userId,
        Guid organizationId,
        SaveMessageTemplateDto request,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveTemplateAsync(
        string userId,
        Guid organizationId,
        Guid templateId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<MessageTemplatePreviewDto> PreviewAsync(
        string userId,
        Guid organizationId,
        PreviewMessageTemplateDto request,
        CancellationToken cancellationToken = default);
}
