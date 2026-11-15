namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Данные для изменения клиента организации. </summary>
public class UpdateOrganizationClientDto
{
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
    public string? StatusCode { get; set; }

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Устаревшее поле: при записи текст объединяется с комментарием; при чтении возвращается null. </summary>
    public string? Notes { get; set; }

    /// <summary> Названия меток клиента. </summary>
    public IReadOnlyList<string> TagNames { get; set; } = [];
}
