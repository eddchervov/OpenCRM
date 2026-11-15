using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Services.Interfaces.Home;

/// <summary> Выдаёт приватные вложения обращений поддержки. </summary>
public interface ISupportAttachmentService
{
    /// <summary> Возвращает вложение только владельцу обращения, иначе возвращает отсутствие результата. </summary>
    Task<SupportAttachmentContentDto?> GetForUserAsync(string userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary> Вызывается только после проверки административного права просмотра обращений. </summary>
    Task<SupportAttachmentContentDto?> GetForAdminAsync(Guid id, CancellationToken cancellationToken = default);
}
