using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Personal;

/// <summary> Модель страницы управления командой и организациями. </summary>
public class TeamViewModel
{
    /// <summary> Сводные данные о командах пользователя. </summary>
    public TeamOverviewDto TeamOverview { get; set; } = new();

    /// <summary> Доступные варианты типов организаций для выбора. </summary>
    public IReadOnlyList<OrganizationTypeOptionDto> OrganizationTypeOptions { get; set; } = [];

    /// <summary> Модель формы создания организации. </summary>
    public CreateOrganizationModel CreateOrganization { get; set; } = new();

    /// <summary> Модель формы приглашения участника в организацию. </summary>
    public InviteOrganizationMemberModel InviteMember { get; set; } = new();

    /// <summary> Сообщение об успешном результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель формы создания организации. </summary>
public class CreateOrganizationModel
{
    /// <summary> Название создаваемой организации. </summary>
    [Required(ErrorMessage = "Укажите название организации.")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание создаваемой организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Код основного сценария создаваемой организации. </summary>
    [Required]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}

/// <summary> Модель приглашения участника в организацию. </summary>
public class InviteOrganizationMemberModel
{
    /// <summary> Идентификатор организации, в которую приглашается участник. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Адрес электронной почты приглашаемого участника. </summary>
    [Required(ErrorMessage = "Укажите email участника.")]
    [EmailAddress(ErrorMessage = "Укажите корректный email.")]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary> Код роли, назначаемой приглашаемому участнику. </summary>
    [Required]
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary> Модель изменения роли участника организации. </summary>
public class UpdateOrganizationMemberRoleModel
{
    /// <summary> Идентификатор организации участника. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор участника организации. </summary>
    [Required]
    public Guid MemberId { get; set; }

    /// <summary> Новый код роли участника. </summary>
    [Required]
    public string RoleCode { get; set; } = string.Empty;
}
