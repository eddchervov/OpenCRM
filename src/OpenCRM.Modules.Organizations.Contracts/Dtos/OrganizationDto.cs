namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> DTO организации с реквизитами, правами и участниками. </summary>
public class OrganizationDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; set; }

    /// <summary> Название организации. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание организации. </summary>
    public string? Description { get; set; }

    /// <summary> Юридическое наименование организации. </summary>
    public string? LegalName { get; set; }

    /// <summary> ИНН организации. </summary>
    public string? Inn { get; set; }

    /// <summary> КПП организации. </summary>
    public string? Kpp { get; set; }

    /// <summary> ОГРН организации. </summary>
    public string? Ogrn { get; set; }

    /// <summary> Юридический адрес организации. </summary>
    public string? LegalAddress { get; set; }

    /// <summary> Фактический адрес организации. </summary>
    public string? ActualAddress { get; set; }

    /// <summary> Телефон организации. </summary>
    public string? Phone { get; set; }

    /// <summary> Email организации. </summary>
    public string? Email { get; set; }

    /// <summary> Должность подписанта. </summary>
    public string? SignatoryPosition { get; set; }

    /// <summary> Имя подписанта. </summary>
    public string? SignatoryName { get; set; }

    /// <summary> Основание полномочий подписанта. </summary>
    public string? SignatoryBasis { get; set; }

    /// <summary> Код стиля документов организации. </summary>
    public string DocumentStyleCode { get; set; } = "auto";

    /// <summary> Режим workflow обслуживания. </summary>
    public string MaintenanceWorkflowMode { get; set; } = "staged";

    /// <summary> Часовой пояс организации для календарных дат и локального расписания. </summary>
    public string? TimeZoneId { get; set; }

    /// <summary> Валюта денежных сумм организации. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Разрешено ли сотрудникам самостоятельно брать работы. </summary>
    public bool AllowWorkSelfAssignment { get; set; }

    /// <summary> Код основного сценария организации. </summary>
    public string PrimaryScenarioCode { get; set; } = string.Empty;

    /// <summary> Название основного сценария организации. </summary>
    public string PrimaryScenarioName { get; set; } = string.Empty;

    /// <summary> Код роли текущего пользователя в организации. </summary>
    public string CurrentUserRoleCode { get; set; } = string.Empty;

    /// <summary> Название роли текущего пользователя. </summary>
    public string CurrentUserRoleName { get; set; } = string.Empty;

    /// <summary> Может ли пользователь управлять участниками. </summary>
    public bool CanManageMembers { get; set; }

    /// <summary> Может ли пользователь управлять настройками. </summary>
    public bool CanManageSettings { get; set; }

    /// <summary> Может ли пользователь управлять биллингом. </summary>
    public bool CanManageBilling { get; set; }

    /// <summary> Может ли пользователь архивировать организацию. </summary>
    public bool CanArchive { get; set; }

    /// <summary> Может ли пользователь восстанавливать организацию из архива. </summary>
    public bool CanRestore { get; set; }

    /// <summary> Количество участников организации. </summary>
    public int MembersCount { get; set; }

    /// <summary> Дата создания организации. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Признак нахождения организации в архиве. </summary>
    public bool IsArchived { get; set; }

    /// <summary> Дата архивирования организации. </summary>
    public DateTime? ArchiveDate { get; set; }

    /// <summary> Количество оставшихся дней до события архива. </summary>
    public int? DaysLeft { get; set; }

    /// <summary> Участники организации. </summary>
    public IReadOnlyList<OrganizationMemberDto> Members { get; set; } = [];

    /// <summary> Ожидающие приглашения организации. </summary>
    public IReadOnlyList<OrganizationInvitationDto> PendingInvitations { get; set; } = [];

    /// <summary> Все приглашения организации. </summary>
    public IReadOnlyList<OrganizationInvitationDto> Invitations { get; set; } = [];
}
