namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Подготовка и подтверждение переноса данных выбранной организации. </summary>
public interface IOrganizationImportService
{
    /// <summary> Проверяет доступность мастера для текущего пользователя. </summary>
    Task<bool> CanImportAsync(string userId, Guid organizationId);

    /// <summary> Возвращает историю загрузок без исходных данных. </summary>
    Task<IReadOnlyList<ImportDraftDto>> ListAsync(string userId, Guid organizationId, CancellationToken cancellationToken);

    /// <summary> Возвращает сохранённый черновик. </summary>
    Task<ImportDraftDto> GetAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken);

    /// <summary> Загружает файл в отдельный черновик. </summary>
    Task<ImportDraftDto> CreateAsync(string userId, Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken);

    /// <summary> Перестраивает строки из исходных таблиц по выбранным правилам. </summary>
    Task<ImportDraftDto> MapAsync(string userId, Guid organizationId, Guid id, int revision, List<ImportTableMapping> mappings, CancellationToken cancellationToken);

    /// <summary> Сохраняет ручные изменения и повторяет проверки. </summary>
    Task<ImportDraftDto> SaveAsync(string userId, Guid organizationId, Guid id, int revision, List<ImportCandidate> rows, CancellationToken cancellationToken);

    /// <summary> Применяет проверенную версию черновика ровно один раз. </summary>
    Task<ImportDraftDto> ConfirmAsync(string userId, Guid organizationId, Guid id, int revision, CancellationToken cancellationToken);

    /// <summary> Возвращает отчёт о действиях над строками. </summary>
    Task<byte[]> GetReportAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken);
}
