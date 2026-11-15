namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO организации в админ-панели. </summary>
public sealed class AdminOrganizationDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; init; }

    /// <summary> Название организации. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Описание организации. </summary>
    public string? Description { get; init; }

    /// <summary> Юридическое наименование. </summary>
    public string? LegalName { get; init; }

    /// <summary> ИНН организации. </summary>
    public string? Inn { get; init; }

    /// <summary> Телефон организации. </summary>
    public string? Phone { get; init; }

    /// <summary> Email организации. </summary>
    public string? Email { get; init; }

    /// <summary> Код основного сценария. </summary>
    public string PrimaryScenarioCode { get; init; } = string.Empty;

    /// <summary> Название сценария. </summary>
    public string ScenarioName { get; init; } = string.Empty;

    /// <summary> Код стиля документов. </summary>
    public string DocumentStyleCode { get; init; } = string.Empty;

    /// <summary> Идентификатор владельца. </summary>
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary> Имя владельца. </summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary> Email владельца. </summary>
    public string? OwnerEmail { get; init; }

    /// <summary> Телефон владельца. </summary>
    public string? OwnerPhone { get; init; }

    /// <summary> Признак активности организации. </summary>
    public bool IsActive { get; init; }

    /// <summary> Признак удаления организации. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Признак архивации организации. </summary>
    public bool IsArchived { get; init; }

    /// <summary> Код статуса организации. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Название статуса организации. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Время создания организации. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время последнего обновления. </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary> Дата архивации. </summary>
    public DateTime? ArchiveDate { get; init; }

    /// <summary> Дата удаления. </summary>
    public DateTime? DeletionDate { get; init; }

    /// <summary> Количество участников. </summary>
    public int MembersCount { get; init; }

    /// <summary> Количество администраторов. </summary>
    public int AdminsCount { get; init; }

    /// <summary> Количество необработанных приглашений. </summary>
    public int PendingInvitationsCount { get; init; }

    /// <summary> Количество клиентов. </summary>
    public int ClientsCount { get; init; }

    /// <summary> Количество транспортных средств. </summary>
    public int VehiclesCount { get; init; }

    /// <summary> Количество обслуживаний. </summary>
    public int MaintenancesCount { get; init; }

    /// <summary> Количество заказов и записей обслуживания. </summary>
    public int WorkOrdersCount { get; init; }

    /// <summary> Количество выполненных заказов. </summary>
    public int CompletedWorkOrdersCount { get; init; }

    /// <summary> Стоимость заказов после скидок. </summary>
    public decimal WorkOrdersTotal { get; init; }

    /// <summary> Оплаченная сумма по заказам. </summary>
    public decimal PaymentsTotal { get; init; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; init; }

    /// <summary> Количество задач планировщика. </summary>
    public int SchedulerTasksCount { get; init; }

    /// <summary> Количество просроченных задач. </summary>
    public int OverdueTasksCount { get; init; }

    /// <summary> Количество позиций склада. </summary>
    public int StockItemsCount { get; init; }

    /// <summary> Количество позиций с низким запасом. </summary>
    public int LowStockItemsCount { get; init; }

    /// <summary> Стоимость запасов. </summary>
    public decimal InventoryValue { get; init; }

    /// <summary> Название активной подписки. </summary>
    public string? ActiveSubscriptionName { get; init; }

    /// <summary> Дата окончания активной подписки. </summary>
    public DateTime? ActiveSubscriptionEndDate { get; init; }

    /// <summary> Время последней записи аудита. </summary>
    public DateTime? LastAuditAt { get; init; }

    /// <summary> Заголовок последней записи аудита. </summary>
    public string? LastAuditTitle { get; init; }

    /// <summary> Список участников организации. </summary>
    public IReadOnlyList<AdminOrganizationMemberDto> Members { get; init; } = [];

    /// <summary> Список необработанных приглашений. </summary>
    public IReadOnlyList<AdminOrganizationInvitationDto> PendingInvitations { get; init; } = [];

    /// <summary> Последние записи аудита. </summary>
    public IReadOnlyList<AdminOrganizationAuditDto> RecentAudit { get; init; } = [];
}

