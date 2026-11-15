namespace OpenCRM.Modules.Planning.Contracts.Dtos;

/// <summary>
/// Напоминание по ручной CRM-задаче планировщика, готовое для UI, email, push или фонового обработчика.
/// </summary>
public sealed class OrganizationSchedulerReminderDto
{
    /// <summary>Идентификатор задачи.</summary>
    public Guid TaskId { get; init; }

    /// <summary>Идентификатор организации.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>Название организации.</summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary>Идентификатор техники, если задача связана с техникой.</summary>
    public Guid? VehicleId { get; init; }

    /// <summary>Идентификатор клиента, если задача связана с клиентом.</summary>
    public Guid? OrganizationClientId { get; init; }

    /// <summary>Ответственный пользователь, которому нужно показать или отправить напоминание.</summary>
    public string? ResponsibleUserId { get; init; }

    /// <summary>Заголовок задачи.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Комментарий задачи.</summary>
    public string? Comment { get; init; }

    /// <summary>Дата и время напоминания.</summary>
    public DateTime ReminderAt { get; init; }

    /// <summary>Плановое начало задачи.</summary>
    public DateTime? StartAt { get; init; }

    /// <summary>Плановое окончание задачи.</summary>
    public DateTime? EndAt { get; init; }

    /// <summary>Понятное название техники.</summary>
    public string? VehicleDisplayName { get; init; }

    /// <summary>Понятное название клиента.</summary>
    public string? ClientName { get; init; }

    /// <summary>Ссылка на задачу в планировщике.</summary>
    public string DetailUrl { get; init; } = string.Empty;
}
