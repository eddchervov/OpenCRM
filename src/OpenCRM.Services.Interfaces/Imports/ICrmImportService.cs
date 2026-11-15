namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Контракт подготовки и выполнения импорта CRM. </summary>
public interface ICrmImportService
{
    /// <summary> Ищет пользователей по строке поиска. </summary>
    Task<object> SearchUsersAsync(string? search, CancellationToken cancellationToken);

    /// <summary> Возвращает организации выбранного пользователя. </summary>
    Task<object> GetOrganizationsAsync(string userId, CancellationToken cancellationToken);

    /// <summary> Создаёт предпросмотр импорта из загруженного файла. </summary>
    Task<CrmImportPreview> CreatePreviewAsync(
        string userId,
        Guid organizationId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken);

    /// <summary> Возвращает сохранённый предпросмотр импорта. </summary>
    Task<CrmImportPreview?> GetPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary> Подтверждает импорт от имени администратора. </summary>
    Task<CrmImportResult> ConfirmAsync(Guid id, string actorUserId, CancellationToken cancellationToken);

    /// <summary> Возвращает отчёт предпросмотра или результата импорта. </summary>
    Task<byte[]?> GetReportAsync(Guid id, bool result, CancellationToken cancellationToken);
}
