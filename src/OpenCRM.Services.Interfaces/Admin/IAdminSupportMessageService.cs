using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Обработка обращений в поддержку. </summary>
public interface IAdminSupportMessageService
{
    /// <summary> Возвращает страницу обращений с фильтрами. </summary>
    Task<AdminSupportMessagesPageDto> GetPageAsync(string? status, string? search, string? source, int page);

    /// <summary> Возвращает обращение в поддержку по идентификатору. </summary>
    Task<AdminSupportMessageDto?> GetAsync(int id);

    /// <summary> Обновляет статус и комментарий администратора у обращения. </summary>
    Task<AdminSupportMessageUpdateResultDto> UpdateAsync(int id, string? statusCode, string? adminComment);

    /// <summary> Отправляет ответ на обращение в поддержку. </summary>
    Task<AdminSupportMessageUpdateResultDto> ReplyAsync(int id, string authorUserId, string? text);
}
