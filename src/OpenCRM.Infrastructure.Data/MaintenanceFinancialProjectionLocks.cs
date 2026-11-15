using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Блокирует заказы и связанные сущности перед пересчётом финансов. </summary>
internal static class MaintenanceFinancialProjectionLocks
{
    internal static bool HasRelevantChanges(OpenCRMContext context) =>
        context.ChangeTracker.Entries<Vehicle>().Any(entry => entry.State == EntityState.Deleted
            || entry.State == EntityState.Modified && (entry.Property(item => item.OrganizationId).IsModified
                || entry.Property(item => item.IsDeleted).IsModified))
        || context.ChangeTracker.Entries<OrganizationClient>().Any(entry => entry.State == EntityState.Deleted)
        || context.ChangeTracker.Entries<Organization>().Any(entry => entry.State == EntityState.Deleted)
        || context.ChangeTracker.Entries<OpenCRM.Domain.Identity.ApplicationUser>().Any(entry => entry.State == EntityState.Deleted);

    internal static async Task<Guid[]> LockAndExpandAsync(
        OpenCRMContext context,
        Guid[] maintenanceIds,
        CancellationToken cancellationToken)
    {
        var vehicleIds = context.ChangeTracker.Entries<Vehicle>()
            .Where(entry => entry.State == EntityState.Deleted
                || entry.State == EntityState.Modified && (entry.Property(item => item.OrganizationId).IsModified
                    || entry.Property(item => item.IsDeleted).IsModified))
            .Select(entry => entry.Entity.Id).ToHashSet();
        var deletedClients = context.ChangeTracker.Entries<OrganizationClient>()
            .Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.Id).ToArray();
        var deletedOrganizations = context.ChangeTracker.Entries<Organization>()
            .Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.Id).ToArray();
        var deletedUsers = context.ChangeTracker.Entries<OpenCRM.Domain.Identity.ApplicationUser>()
            .Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.Id).ToArray();
        if (vehicleIds.Count == 0 && deletedClients.Length == 0
            && deletedOrganizations.Length == 0 && deletedUsers.Length == 0)
        {
            await context.Database.ExecuteSqlAsync($"""
                SELECT "Id" FROM "Maintenance" WHERE "Id" = ANY ({maintenanceIds}) ORDER BY "Id" FOR NO KEY UPDATE
                """, cancellationToken).ConfigureAwait(false);

            return maintenanceIds;
        }

        if (deletedOrganizations.Length > 0)
        {
            var cascadeClients = await context.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM "OrganizationClients" WHERE "OrganizationId" = ANY ({deletedOrganizations})
                """).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            deletedClients = deletedClients.Concat(cascadeClients).Distinct().ToArray();
        }

        var cascadeVehicles = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "Vehicles"
            WHERE "OrganizationId" = ANY ({deletedOrganizations}) OR "UserId" = ANY ({deletedUsers})
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        vehicleIds.UnionWith(cascadeVehicles);
        var lockedVehicleIds = vehicleIds.ToArray();
        var expandedIds = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "Maintenance"
            WHERE "VehicleId" = ANY ({lockedVehicleIds}) OR "BuyerClientId" = ANY ({deletedClients})
                OR "UserId" = ANY ({deletedUsers})
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        var allIds = maintenanceIds.Concat(expandedIds).Distinct().ToArray();
        await context.Database.ExecuteSqlAsync($"""
            SELECT "Id" FROM "Maintenance" WHERE "Id" = ANY ({allIds}) ORDER BY "Id" FOR NO KEY UPDATE
            """, cancellationToken).ConfigureAwait(false);
        if (deletedUsers.Length > 0 || deletedOrganizations.Length > 0)
        {
            await context.Database.ExecuteSqlAsync($"""
                SELECT "Id" FROM identity."AspNetUsers" WHERE "Id" = ANY ({deletedUsers}) ORDER BY "Id" FOR UPDATE
                """, cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlAsync($"""
                SELECT "Id" FROM "Organizations" WHERE "Id" = ANY ({deletedOrganizations}) ORDER BY "Id" FOR UPDATE
                """, cancellationToken).ConfigureAwait(false);
            var insertedVehicles = await context.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM "Vehicles"
                WHERE "UserId" = ANY ({deletedUsers}) OR "OrganizationId" = ANY ({deletedOrganizations})
                """).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            vehicleIds.UnionWith(insertedVehicles);
            lockedVehicleIds = vehicleIds.ToArray();
            if (deletedOrganizations.Length > 0)
            {
                var insertedClients = await context.Database.SqlQuery<Guid>($"""
                    SELECT "Id" AS "Value" FROM "OrganizationClients" WHERE "OrganizationId" = ANY ({deletedOrganizations})
                    """).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                deletedClients = deletedClients.Concat(insertedClients).Distinct().ToArray();
            }
        }

        await context.Database.ExecuteSqlAsync($"""
            SELECT "Id" FROM "Vehicles" WHERE "Id" = ANY ({lockedVehicleIds}) ORDER BY "Id" FOR UPDATE
            """, cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlAsync($"""
            SELECT "Id" FROM "OrganizationClients" WHERE "Id" = ANY ({deletedClients}) ORDER BY "Id" FOR UPDATE
            """, cancellationToken).ConfigureAwait(false);
        // После блокировки внешних ключей учитываем заказы, вставленные между первым чтением и блокировкой.
        var insertedIds = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "Maintenance"
            WHERE "VehicleId" = ANY ({lockedVehicleIds}) OR "BuyerClientId" = ANY ({deletedClients})
                OR "UserId" = ANY ({deletedUsers})
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        allIds = allIds.Concat(insertedIds).Distinct().ToArray();
        await context.Database.ExecuteSqlAsync($"""
            SELECT "Id" FROM "Maintenance" WHERE "Id" = ANY ({allIds}) ORDER BY "Id" FOR NO KEY UPDATE
            """, cancellationToken).ConfigureAwait(false);

        return allIds;
    }
}
