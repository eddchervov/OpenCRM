using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Organizations;

/// <summary> Загрузка организаций с проверкой доступа. </summary>
internal sealed class OrganizationAccess(OpenCRMContext context)
{
    /// <summary> Возвращает организацию для управления пользователем. </summary>
    internal async Task<Organization> GetForManageAsync(string userId, Guid organizationId)
    {
        var organization = await this.GetActiveAsync(organizationId);

        if (!organization.Members.Any(member => member.UserId == userId))
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        return organization;
    }

    /// <summary> Возвращает активную организацию по идентификатору. </summary>
    internal async Task<Organization> GetActiveAsync(Guid organizationId)
    {
        var organization = await this.LoadAsync(organizationId);
        if (organization.IsDeleted || !organization.IsActive || organization.IsArchived)
        {
            throw new InvalidOperationException("Организация не найдена.");
        }

        return organization;
    }

    /// <summary> Возвращает организацию для восстановления из архива. </summary>
    internal async Task<Organization> GetForRestoreAsync(Guid organizationId)
    {
        var organization = await this.LoadAsync(organizationId);
        if (organization.IsDeleted || !organization.IsArchived)
        {
            throw new InvalidOperationException("Организация не найдена.");
        }

        return organization;
    }

    /// <summary> Загружает организацию по идентификатору с проверкой. </summary>
    private async Task<Organization> LoadAsync(Guid organizationId)
    {
        var organization = await context.Organizations
            .FirstOrDefaultAsync(value => value.Id == organizationId)
            ?? throw new InvalidOperationException("Организация не найдена.");
        await context.Entry(organization).ReloadAsync();
        if (context.Entry(organization).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Организация не найдена.");
        }

        var trackedMembers = context.ChangeTracker.Entries<OrganizationMember>()
            .Where(entry => entry.Entity.OrganizationId == organizationId)
            .ToList();

        foreach (var entry in trackedMembers)
        {
            entry.State = EntityState.Detached;
        }

        var trackedInvitations = context.ChangeTracker.Entries<OrganizationInvitation>()
            .Where(entry => entry.Entity.OrganizationId == organizationId)
            .ToList();

        foreach (var entry in trackedInvitations)
        {
            entry.State = EntityState.Detached;
        }

        organization.Members.Clear();
        organization.Invitations.Clear();
        await context.Entry(organization).Collection(value => value.Members).Query().LoadAsync();
        await context.Entry(organization).Collection(value => value.Invitations).Query().LoadAsync();

        return organization;
    }
}
