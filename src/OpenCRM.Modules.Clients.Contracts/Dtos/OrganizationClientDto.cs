using OpenCRM.Modules.Clients.Contracts;

namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Клиент организации. </summary>
public class OrganizationClientDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Наименование клиента. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Фамилия физического лица; null для прежнего неразделённого имени. </summary>
    public string? LastName { get; set; }

    /// <summary> Имя физического лица. </summary>
    public string? FirstName { get; set; }

    /// <summary> Отчество физического лица, если есть. </summary>
    public string? MiddleName { get; set; }

    /// <summary> Вид организации: legal_entity или entrepreneur; null для прежнего контракта. </summary>
    public string? CompanyTypeCode { get; set; }

    /// <summary> Телефон клиента. </summary>
    public string? Phone { get; set; }

    /// <summary> Код типа контрагента. </summary>
    public string? PartyTypeCode { get; set; }

    /// <summary> ИНН клиента. </summary>
    public string? Inn { get; set; }

    /// <summary> КПП клиента. </summary>
    public string? Kpp { get; set; }

    /// <summary> Юридический адрес клиента. </summary>
    public string? LegalAddress { get; set; }

    /// <summary> Комментарий по клиенту. </summary>
    public string? Comment { get; set; }

    /// <summary> Код статуса клиента. </summary>
    public string StatusCode { get; set; } = OrganizationClientStatusCodes.Active;

    /// <summary> Отображаемое название статуса клиента. </summary>
    public string StatusName { get; set; } = OrganizationClientStatusCodes.GetName(OrganizationClientStatusCodes.Active);

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Отображаемое имя ответственного. </summary>
    public string? ResponsibleDisplayName { get; set; }

    /// <summary> Устаревшее поле: при записи текст объединяется с комментарием; при чтении возвращается null. </summary>
    public string? Notes { get; set; }

    /// <summary> Метки клиента. </summary>
    public IReadOnlyList<OrganizationClientTagDto> Tags { get; set; } = [];

    /// <summary> Количество транспортных средств клиента. </summary>
    public int VehiclesCount { get; set; }

    /// <summary> Количество обслуживаний клиента. </summary>
    public int MaintenanceCount { get; set; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastMaintenanceDate { get; set; }

    /// <summary> Сумма оплат клиента, если пользователю разрешено просматривать финансы. </summary>
    public decimal? TotalPaidAmount { get; set; }

    public string CurrencyCode { get; set; } = "RUB";

    public IReadOnlyList<OrganizationClientCurrencyTotalsDto> OtherCurrencyTotals { get; set; } = [];

    /// <summary> Общий долг клиента, если пользователю разрешено просматривать финансы. </summary>
    public decimal? TotalDebtAmount { get; set; }

    /// <summary> Транспортные средства клиента. </summary>
    public IReadOnlyList<OrganizationClientVehicleDto> Vehicles { get; set; } = [];

    /// <summary> Дата создания клиента. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Признак нахождения в архиве. </summary>
    public bool IsArchived { get; set; }

    /// <summary> Дата архивирования. </summary>
    public DateTime? ArchivedAt { get; set; }
}
