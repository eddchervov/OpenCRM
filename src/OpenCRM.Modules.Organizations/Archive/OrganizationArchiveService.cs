using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Archive;

/// <summary> Сервис архивирования и восстановления организаций. </summary>
internal sealed class OrganizationArchiveService(
    OpenCRMContext context,
    IOrganizationPermissionService permissions,
    IConfiguration configuration,
    OrganizationAccess access,
    OrganizationAuditWriter audit,
    OrganizationTransaction transaction) : IOrganizationArchiveService
{
    /// <summary> Срок хранения организации в архиве в днях. </summary>
    private const int ArchiveRetentionDays = 30;

    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость проверки разрешений организации. </summary>
    private readonly IOrganizationPermissionService _permissions = permissions;

    /// <summary> Конфигурация доступности архивирования. </summary>
    private readonly IConfiguration _configuration = configuration;

    /// <summary> Зависимость доступа к организации. </summary>
    private readonly OrganizationAccess _access = access;

    /// <summary> Зависимость записи событий аудита. </summary>
    private readonly OrganizationAuditWriter _audit = audit;

    /// <summary> Зависимость выполнения операций в транзакции. </summary>
    private readonly OrganizationTransaction _transaction = transaction;

    /// <summary> Архивирует организацию от имени пользователя. </summary>
    public Task ArchiveAsync(string currentUserId, Guid organizationId)
    {
        if (!string.Equals(this._configuration["Organizations:ArchivingEnabled"], bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Архивирование организаций временно отключено.");
        }

        return this._transaction.ExecuteAuditedAsync(eventId => this.ArchiveCoreAsync(currentUserId, organizationId, eventId));
    }

    /// <summary> Внутренняя реализация архивирования организации. </summary>
    private async Task ArchiveCoreAsync(string currentUserId, Guid organizationId, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._permissions.EnsurePermissionAsync(currentUserId, organizationId, OrganizationPermissionCodes.WorkspaceArchive);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);
        organization.Archive(DateTime.UtcNow);
        await this._audit.AddAsync(
            organization.Id,
            currentUserId,
            "organization.archived",
            "Организация отправлена в архив",
            organization.Name,
            saveChanges: false, eventId: eventId);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Восстанавливает организацию из архива. </summary>
    public Task RestoreAsync(string currentUserId, Guid organizationId) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.RestoreCoreAsync(currentUserId, organizationId, eventId));

    /// <summary> Архивирует организацию по решению администратора с обязательным аудитом причины. </summary>
    public Task ArchiveByAdminAsync(string adminUserId, Guid organizationId, string reason)
    {
        var normalizedReason = OrganizationArchiveService.NormalizeAdminReason(adminUserId, reason);

        return this._transaction.ExecuteAuditedAsync(eventId =>
            this.ChangeArchiveStateByAdminAsync(adminUserId, organizationId, normalizedReason, true, eventId));
    }

    /// <summary> Восстанавливает неудалённую организацию по решению администратора с аудитом причины. </summary>
    public Task RestoreByAdminAsync(string adminUserId, Guid organizationId, string reason)
    {
        var normalizedReason = OrganizationArchiveService.NormalizeAdminReason(adminUserId, reason);

        return this._transaction.ExecuteAuditedAsync(eventId =>
            this.ChangeArchiveStateByAdminAsync(adminUserId, organizationId, normalizedReason, false, eventId));
    }

    private async Task ChangeArchiveStateByAdminAsync(
        string adminUserId, Guid organizationId, string reason, bool shouldArchive, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        var organization = await this._context.Organizations
            .SingleOrDefaultAsync(item => item.Id == organizationId && !item.IsDeleted);
        if (organization == null)
        {
            throw new InvalidOperationException("Организация не найдена или уже удалена.");
        }

        if (organization.IsArchived == shouldArchive)
        {
            throw new InvalidOperationException(shouldArchive
                ? "Организация уже находится в архиве."
                : "Организация не находится в архиве.");
        }

        var now = DateTime.UtcNow;
        if (shouldArchive)
        {
            organization.Archive(now);
        }
        else
        {
            organization.Restore(now);
        }

        var action = shouldArchive ? "archived" : "restored";
        var title = shouldArchive ? "Администратор архивировал организацию" : "Администратор восстановил организацию";
        await this._audit.AddAsync(
            organization.Id,
            adminUserId,
            $"organization.admin_{action}",
            title,
            $"Причина: {reason}",
            saveChanges: false,
            eventId: eventId);
        await this._context.SaveChangesAsync();
    }

    private static string NormalizeAdminReason(string adminUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 2_000)
        {
            throw new ArgumentException("Причина не должна превышать 2000 символов.", nameof(reason));
        }

        return normalizedReason;
    }

    /// <summary> Внутренняя реализация восстановления организации. </summary>
    private async Task RestoreCoreAsync(string currentUserId, Guid organizationId, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        var organization = await this._access.GetForRestoreAsync(organizationId);

        OrganizationRules.EnsureOwner(currentUserId, organization);
        organization.Restore(DateTime.UtcNow);
        await this._audit.AddAsync(
            organization.Id,
            currentUserId,
            "organization.restored",
            "Организация восстановлена из архива",
            organization.Name,
            saveChanges: false, eventId: eventId);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Выполняет очистку архива по сроку хранения. </summary>
    public async Task CleanUpArchiveAsync()
    {
        var now = DateTime.UtcNow;
        var threshold = now.AddDays(-ArchiveRetentionDays);
        await this._context.Organizations
            .Where(x => !x.IsDeleted && x.IsArchived && x.ArchiveDate.HasValue && x.ArchiveDate.Value <= threshold)
            .ExecuteUpdateAsync(update => update
                .SetProperty(organization => organization.IsDeleted, true)
                .SetProperty(organization => organization.DeletionDate, now)
                .SetProperty(organization => organization.IsArchived, false)
                .SetProperty(organization => organization.IsActive, false)
                .SetProperty(organization => organization.UpdatedAt, now));
    }
}
