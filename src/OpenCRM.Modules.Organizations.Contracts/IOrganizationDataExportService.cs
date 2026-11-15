namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Переносимая выгрузка рабочих данных организации для её владельца. </summary>
public interface IOrganizationDataExportService
{
    /// <summary> Возвращает существующее задание либо ставит подготовку архива в очередь. </summary>
    Task<OrganizationDataExportStatusDto> RequestAsync(string userId, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary> Возвращает последнее состояние выгрузки текущему владельцу. </summary>
    Task<OrganizationDataExportStatusDto> GetStatusAsync(string userId, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary> Открывает готовый архив. Вызывающий код обязан освободить возвращённый поток. </summary>
    Task<Stream> OpenAsync(string userId, Guid organizationId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary> Обрабатывает очередь под общей межпроцессной блокировкой. </summary>
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}
