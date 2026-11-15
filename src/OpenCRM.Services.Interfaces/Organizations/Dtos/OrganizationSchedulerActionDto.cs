namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Действие, доступное для записи планировщика. </summary>
public sealed class OrganizationSchedulerActionDto
{
    /// <summary> Код действия. </summary>
    public string ActionCode { get; init; } = string.Empty;

    /// <summary> Адрес для выполнения действия. </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary> Код стиля оформления действия. </summary>
    public string StyleCode { get; init; } = string.Empty;

    /// <summary> Признак действия workflow-процесса. </summary>
    public bool IsWorkflowAction { get; init; }

    /// <summary> Признак отправки запроса методом POST. </summary>
    public bool IsPost { get; init; }

    /// <summary> Код источника записи. </summary>
    public string? SourceCode { get; set; }

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор связанной сущности-источника. </summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary> Ключ триггера действия. </summary>
    public string? TriggerKey { get; set; }

    /// <summary> Текст подтверждения перед выполнением действия. </summary>
    public string? ConfirmMessage { get; init; }
}
