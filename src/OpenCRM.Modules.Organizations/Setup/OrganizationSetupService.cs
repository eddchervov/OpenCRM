using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Setup;

/// <summary> Сервис первоначальной настройки организации. </summary>
internal sealed class OrganizationSetupService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationPermissionService organizationPermissionService,
    ICatalogSetupService catalogSetupService,
    OrganizationWorkingSchedule workingSchedule,
    OrganizationTransaction organizationTransaction) : IOrganizationSetupService
{
    /// <summary> Возвращает данные настройки организации. </summary>
    public async Task<OrganizationSetupDto> GetSetupAsync(string userId, Guid organizationId)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceView);

        var organization = await context.Organizations
            .AsNoTracking()
            .SingleAsync(value => value.Id == organizationId && !value.IsDeleted);
        var workingDays = await context.OrganizationWorkingDays
            .AsNoTracking()
            .Include(day => day.Breaks)
            .Where(day => day.OrganizationId == organizationId)
            .ToListAsync();
        var canManage = await organizationPermissionService.HasPermissionAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        var weeklySchedule = workingDays.Count == 0
            ? OrganizationWorkingSchedule.CreateLegacy(organization)
            : OrganizationWorkingSchedule.Create(workingDays);
        var scheduledDays = weeklySchedule.Where(day => day.IsWorking).ToList();

        return new OrganizationSetupDto
        {
            ShouldAutoOpen = organization.SetupAutoOpenPending && organization.SetupCompletedAt == null,
            Step = organization.SetupStep,
            Revision = organization.SetupRevision,
            CompletedAt = organization.SetupCompletedAt,
            CanManage = canManage,
            WorkdayStart = scheduledDays.Count == 0 ? organization.WorkdayStart : scheduledDays.Min(day => day.StartTime),
            WorkdayEnd = scheduledDays.Count == 0 ? organization.WorkdayEnd : scheduledDays.Max(day => day.EndTime),
            WorkingDays = scheduledDays.Count == 0
                ? organization.WorkingDays
                : scheduledDays.Select(day => day.DayOfWeek).ToArray(),
            WeeklySchedule = weeklySchedule
        };
    }

    /// <summary> Открывает настройку организации, возвращает успех. </summary>
    public async Task<bool> TryOpenSetupAsync(string userId, Guid organizationId)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        var eventId = Guid.NewGuid();

        return await organizationTransaction.ExecuteAsync(async () =>
        {
            var organization = await context.Organizations
                .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
                .SingleOrDefaultAsync();
            if (organization is null)
            {
                return false;
            }

            await context.Entry(organization).ReloadAsync();
            if (organization.IsDeleted || organization.IsArchived || !organization.IsActive
                || !organization.SetupAutoOpenPending || organization.SetupCompletedAt != null)
            {
                return false;
            }

            await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                userId,
                organizationId,
                OrganizationPermissionCodes.WorkspaceProfileEdit);
            organization.SetupAutoOpenPending = false;
            context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                Id = eventId,
                OrganizationId = organization.Id,
                ActorUserId = userId,
                EventType = "organization.setup.opened",
                Title = "Первичная настройка открыта",
                TargetType = "organization",
                TargetId = organization.Id.ToString("D")
            });
            await context.SaveChangesAsync();

            return true;
        }, () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(auditEvent => auditEvent.Id == eventId));
    }

    /// <summary> Сохраняет настройки организации. </summary>
    public async Task SaveSetupAsync(string userId, Guid organizationId, SaveOrganizationSetupDto dto)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        OrganizationSetupService.ValidateSetup(dto);

        await organizationTransaction.ExecuteAsync(() => this.SaveSetupCoreAsync(userId, organizationId, dto));
    }

    /// <summary> Внутренняя реализация сохранения настроек. </summary>
    private async Task SaveSetupCoreAsync(string userId, Guid organizationId, SaveOrganizationSetupDto dto)
    {
        var organization = await context.Organizations
            .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
            .SingleAsync();
        await context.Entry(organization).ReloadAsync();
        if (organization.IsDeleted || organization.IsArchived || !organization.IsActive)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        if (organization.SetupRevision != dto.Revision)
        {
            throw new DbUpdateConcurrencyException("Настройка уже изменена. Откройте её заново.");
        }

        if (!dto.Skip && dto.Step < 2)
        {
            var kindCode = dto.Step == 0
                ? OrganizationScenarioCatalogKindCodes.WorkPost
                : OrganizationScenarioCatalogKindCodes.Service;
            var items = dto.Items.Select(item => new CatalogSetupItemDto
            {
                Id = item.Id,
                Name = item.Name,
                UnitPrice = item.UnitPrice,
                DurationMinutes = item.DurationMinutes,
                IsActive = item.IsActive
            }).ToArray();
            await catalogSetupService.SaveItemsAsync(organization.Id, kindCode, items);
        }

        if (!dto.Skip && dto.Step == 2)
        {
            await workingSchedule.SaveAsync(organization, dto);
        }

        organization.SetupStep = Math.Max(organization.SetupStep, Math.Min(dto.Step + 1, 3));
        if (dto.Step == 3)
        {
            organization.SetupCompletedAt ??= DateTime.UtcNow;
        }

        organization.SetupAutoOpenPending = false;
        organization.SetupRevision++;
        organization.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    /// <summary> Проверяет корректность данных настройки. </summary>
    private static void ValidateSetup(SaveOrganizationSetupDto dto)
    {
        if (dto.Step is < 0 or > 3 || dto.Items == null || dto.Items.Count > 100)
        {
            throw new InvalidOperationException("Проверьте шаг настройки и количество позиций.");
        }
    }
}
