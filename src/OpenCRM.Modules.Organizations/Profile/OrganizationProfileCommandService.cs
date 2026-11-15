using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Profile;

/// <summary> Сервис команд профиля организации. </summary>
internal sealed class OrganizationProfileCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    OrganizationScenarioReader scenarios,
    OrganizationAccess access,
    OrganizationAuditWriter audit,
    OrganizationTransaction transaction) : IOrganizationProfileCommandService
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость сервиса подписок организации. </summary>
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;

    /// <summary> Зависимость чтения сценариев организации. </summary>
    private readonly OrganizationScenarioReader _scenarios = scenarios;

    /// <summary> Зависимость доступа к организации. </summary>
    private readonly OrganizationAccess _access = access;

    /// <summary> Зависимость записи событий аудита. </summary>
    private readonly OrganizationAuditWriter _audit = audit;

    /// <summary> Зависимость выполнения операций в транзакции. </summary>
    private readonly OrganizationTransaction _transaction = transaction;

    /// <summary> Создаёт организацию от имени владельца. </summary>
    public Task<Guid> CreateAsync(string ownerUserId, CreateOrganizationDto dto)
    {
        var organizationId = Guid.NewGuid();

        return this._transaction.ExecuteAsync(
            () => this.CreateCoreAsync(ownerUserId, dto, organizationId),
            () => this._context.Organizations.AsNoTracking().AnyAsync(organization => organization.Id == organizationId));
    }

    /// <summary> Внутренняя реализация создания организации. </summary>
    private async Task<Guid> CreateCoreAsync(string ownerUserId, CreateOrganizationDto dto, Guid organizationId)
    {
        var scenarioCode = await this._scenarios.NormalizeAsync(dto.PrimaryScenarioCode);
        await this._context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM identity."AspNetUsers" WHERE "Id" = {ownerUserId} FOR NO KEY UPDATE""");

        var ownerAlreadyHasScenario = await this._context.Organizations
            .AsNoTracking()
            .AnyAsync(organization => organization.OwnerUserId == ownerUserId
                && organization.PrimaryScenarioCode == scenarioCode
                && !organization.IsDeleted);
        if (ownerAlreadyHasScenario)
        {
            throw new InvalidOperationException(
                "На один аккаунт доступна одна организация каждого типа. Если организация уже в архиве, восстановите её на странице «Организации».");
        }

        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            Id = organizationId,
            Name = OrganizationRules.NormalizeRequired(dto.Name, "Название организации обязательно."),
            Description = OrganizationRules.NormalizeNullable(dto.Description),
            PrimaryScenarioCode = scenarioCode,
            SetupAutoOpenPending = true,
            OwnerUserId = ownerUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        organization.Members.Add(new OrganizationMember
        {
            UserId = ownerUserId,
            RoleCode = OrganizationRoleCodes.Owner,
            InvitedByUserId = ownerUserId,
            CreatedAt = now,
            JoinedAt = now
        });

        this._context.Organizations.Add(organization);
        await this._context.SaveChangesAsync();

        return organization.Id;
    }

    /// <summary> Обновляет основные данные организации. </summary>
    public Task UpdateAsync(string currentUserId, Guid organizationId, UpdateOrganizationDto dto) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.UpdateCoreAsync(currentUserId, organizationId, dto, eventId));

    /// <summary> Внутренняя реализация обновления организации. </summary>
    private async Task UpdateCoreAsync(string currentUserId, Guid organizationId, UpdateOrganizationDto dto, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);
        OrganizationRules.ValidateRequisites(dto.Inn, dto.Kpp, dto.Ogrn);
        var previousName = organization.Name;
        var previousScenarioCode = organization.PrimaryScenarioCode;
        await this._scenarios.EnsureUnchangedAsync(previousScenarioCode, dto.PrimaryScenarioCode);

        var documentStyleCode = OrganizationRules.NormalizeDocumentStyle(dto.DocumentStyleCode);
        await this.EnsureProfileFeaturesAsync(currentUserId, organization, documentStyleCode, organization.AllowWorkSelfAssignment);

        organization.Name = OrganizationRules.NormalizeRequired(dto.Name, "Название организации обязательно.");
        organization.Description = OrganizationRules.NormalizeNullable(dto.Description);
        organization.LegalName = OrganizationRules.NormalizeNullable(dto.LegalName);
        organization.Inn = OrganizationRules.NormalizeNullable(dto.Inn);
        organization.Kpp = OrganizationRules.NormalizeNullable(dto.Kpp)?.ToUpperInvariant();
        organization.Ogrn = OrganizationRules.NormalizeNullable(dto.Ogrn);
        organization.LegalAddress = OrganizationRules.NormalizeNullable(dto.LegalAddress);
        organization.ActualAddress = OrganizationRules.NormalizeNullable(dto.ActualAddress);
        organization.Phone = OrganizationRules.NormalizeNullable(dto.Phone);
        organization.Email = OrganizationRules.NormalizeNullable(dto.Email);
        organization.SignatoryPosition = OrganizationRules.NormalizeNullable(dto.SignatoryPosition);
        organization.SignatoryName = OrganizationRules.NormalizeNullable(dto.SignatoryName);
        organization.SignatoryBasis = OrganizationRules.NormalizeNullable(dto.SignatoryBasis);
        organization.DocumentStyleCode = documentStyleCode;
        organization.MaintenanceWorkflowMode = OrganizationRules.NormalizeMaintenanceWorkflowMode(dto.MaintenanceWorkflowMode ?? organization.MaintenanceWorkflowMode);
        organization.UpdatedAt = DateTime.UtcNow;
        await this._audit.AddAsync(
            organization.Id,
            currentUserId,
            "organization.settings.updated",
            "Настройки организации изменены",
            OrganizationRules.BuildOrganizationChangeDescription(previousName, organization.Name, previousScenarioCode, organization.PrimaryScenarioCode),
            saveChanges: false, eventId: eventId);

        await this._context.SaveChangesAsync();
    }

    /// <summary> Обновляет публичный профиль организации. </summary>
    public Task UpdateProfileAsync(string currentUserId, Guid organizationId, UpdateOrganizationProfileDto dto) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.UpdateProfileCoreAsync(currentUserId, organizationId, dto, eventId));

    /// <summary> Внутренняя реализация обновления профиля. </summary>
    private async Task UpdateProfileCoreAsync(string currentUserId, Guid organizationId, UpdateOrganizationProfileDto dto, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);
        var previousName = organization.Name;
        var previousScenarioCode = organization.PrimaryScenarioCode;
        await this._scenarios.EnsureUnchangedAsync(previousScenarioCode, dto.PrimaryScenarioCode);

        var documentStyleCode = OrganizationRules.NormalizeDocumentStyle(dto.DocumentStyleCode);
        await this.EnsureProfileFeaturesAsync(currentUserId, organization, documentStyleCode, dto.AllowWorkSelfAssignment);

        organization.Name = OrganizationRules.NormalizeRequired(dto.Name, "Название организации обязательно.");
        organization.Description = OrganizationRules.NormalizeNullable(dto.Description);
        organization.DocumentStyleCode = documentStyleCode;
        organization.MaintenanceWorkflowMode = OrganizationRules.NormalizeMaintenanceWorkflowMode(dto.MaintenanceWorkflowMode ?? organization.MaintenanceWorkflowMode);
        organization.AllowWorkSelfAssignment = dto.AllowWorkSelfAssignment;
        organization.UpdatedAt = DateTime.UtcNow;
        await this._audit.AddAsync(
            organization.Id,
            currentUserId,
            "organization.profile.updated",
            "Профиль организации изменен",
            OrganizationRules.BuildOrganizationChangeDescription(previousName, organization.Name, previousScenarioCode, organization.PrimaryScenarioCode),
            saveChanges: false, eventId: eventId);

        await this._context.SaveChangesAsync();
    }

    /// <summary> Проверяет платные изменения настроек, сохраняя прежние значения после окончания подписки. </summary>
    private async Task EnsureProfileFeaturesAsync(string userId, Organization organization, string documentStyleCode, bool allowWorkSelfAssignment)
    {
        if (organization.PrimaryScenarioCode is not (OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation))
        {
            return;
        }

        if (organization.DocumentStyleCode != documentStyleCode
            && !await this._subscriptions.HasFeatureAsync(userId, organization.Id, FeatureCodes.DocumentStyling))
        {
            throw new InvalidOperationException("Оформление документов доступно в PRO.");
        }

        if (organization.AllowWorkSelfAssignment != allowWorkSelfAssignment
            && !await this._subscriptions.HasFeatureAsync(userId, organization.Id, FeatureCodes.MembersModule))
        {
            throw new InvalidOperationException("Самостоятельное назначение работ доступно в PRO.");
        }
    }

    /// <summary> Обновляет реквизиты организации. </summary>
    public Task UpdateRequisitesAsync(string currentUserId, Guid organizationId, UpdateOrganizationRequisitesDto dto) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.UpdateRequisitesCoreAsync(currentUserId, organizationId, dto, eventId));

    /// <summary> Внутренняя реализация обновления реквизитов. </summary>
    private async Task UpdateRequisitesCoreAsync(string currentUserId, Guid organizationId, UpdateOrganizationRequisitesDto dto, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.WorkspaceRequisitesEdit);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);
        OrganizationRules.ValidateRequisites(dto.Inn, dto.Kpp, dto.Ogrn);
        organization.LegalName = OrganizationRules.NormalizeNullable(dto.LegalName);
        organization.Inn = OrganizationRules.NormalizeNullable(dto.Inn);
        organization.Kpp = OrganizationRules.NormalizeNullable(dto.Kpp)?.ToUpperInvariant();
        organization.Ogrn = OrganizationRules.NormalizeNullable(dto.Ogrn);
        organization.LegalAddress = OrganizationRules.NormalizeNullable(dto.LegalAddress);
        organization.ActualAddress = OrganizationRules.NormalizeNullable(dto.ActualAddress);
        organization.Phone = OrganizationRules.NormalizeNullable(dto.Phone);
        organization.Email = OrganizationRules.NormalizeNullable(dto.Email);
        organization.SignatoryPosition = OrganizationRules.NormalizeNullable(dto.SignatoryPosition);
        organization.SignatoryName = OrganizationRules.NormalizeNullable(dto.SignatoryName);
        organization.SignatoryBasis = OrganizationRules.NormalizeNullable(dto.SignatoryBasis);
        organization.UpdatedAt = DateTime.UtcNow;
        await this._audit.AddAsync(
            organization.Id,
            currentUserId,
            "organization.requisites.updated",
            "Реквизиты организации изменены",
            "Обновлены юридические реквизиты или контактные данные.",
            saveChanges: false, eventId: eventId);

        await this._context.SaveChangesAsync();
    }
}
