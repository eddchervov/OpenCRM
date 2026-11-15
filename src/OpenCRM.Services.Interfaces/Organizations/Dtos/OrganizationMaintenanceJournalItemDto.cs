using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Запись журнала с составом услуг и датами её выполнения. </summary>
public sealed class OrganizationMaintenanceJournalItemDto
{
    /// <summary> Идентификатор записи журнала обслуживания. </summary>
    public Guid Id { get; init; }

    /// <summary> Отображаемое название техники. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Регистрационный номерной знак техники. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> Имя клиента. </summary>
    public string? ClientName { get; init; }

    /// <summary> Наименования услуг записи. </summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary> Строки услуг в сохранённом порядке. </summary>
    public IReadOnlyList<OrganizationMaintenanceJournalServiceLineDto> ServiceLines { get; init; } = [];

    /// <summary> Исполнители записи и отдельных услуг. </summary>
    public IReadOnlyList<string> PerformerNames { get; init; } = [];

    /// <summary> Статус обслуживания. </summary>
    public MaintenanceStatus Status { get; init; }

    /// <summary> Отображаемое название статуса. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Плановая дата обслуживания. </summary>
    public DateTime? PlannedDate { get; init; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; init; }

    /// <summary> Дата завершения обслуживания. </summary>
    public DateTime? CompletedDate { get; init; }

    /// <summary> Эффективная дата записи: завершение для выполненной работы или план для остальных. </summary>
    public DateTime Date { get; init; }

    /// <summary> Номер акта, если он присвоен. </summary>
    public int? WorkActNumber { get; init; }

    /// <summary> Сумма по записи обслуживания. </summary>
    public decimal Amount { get; init; }

    /// <summary> Оплаченная сумма, если у пользователя есть финансовое право. </summary>
    public decimal? PaidAmount { get; init; }

    /// <summary> Статус оплаты, если у пользователя есть финансовое право. </summary>
    public string? PaymentStatus { get; init; }
}
