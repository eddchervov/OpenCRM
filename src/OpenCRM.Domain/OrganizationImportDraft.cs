namespace OpenCRM.Domain;

/// <summary> Сохранённый черновик переноса клиентов и техники организации. </summary>
public sealed class OrganizationImportDraft
{
    /// <summary> Постоянное имя базы исходной CRM в пределах организации. </summary>
    public string? SourceKey { get; set; }

    /// <summary> Идентификатор черновика. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Организация назначения. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Автор загрузки файла. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Имя исходного файла. </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary> Версия данных, проверенных перед подтверждением. </summary>
    public int Revision { get; set; } = 1;

    /// <summary> Исходные таблицы, правила и редактируемые строки пакета. </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary> Зафиксированный результат переноса. </summary>
    public string? ResultJson { get; set; }

    /// <summary> Дата загрузки. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата последнего сохранения. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата завершения переноса. </summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary> Версия хранения: исходные JSON-черновики или построчная подготовка. </summary>
    public int StorageVersion { get; set; } = 1;

    /// <summary> Состояние фоновой подготовки и применения. </summary>
    public string State { get; set; } = "ready";

    /// <summary> Последняя понятная пользователю ошибка фонового задания. </summary>
    public string? Error { get; set; }

    /// <summary> Количество подготовленных строк. </summary>
    public int TotalRows { get; set; }

    /// <summary> Количество строк, обработанных при применении. </summary>
    public int ProcessedRows { get; set; }

    /// <summary> Пользователь, подтвердивший текущую фоновую операцию. </summary>
    public string? RequestedBy { get; set; }

    /// <summary> Этап, который можно повторить после сбоя. </summary>
    public string? ResumeState { get; set; }
}
