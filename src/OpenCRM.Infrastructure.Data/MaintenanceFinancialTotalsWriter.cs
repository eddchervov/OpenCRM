using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Пересчитывает финансовые показатели затронутых заказов в транзакции их сохранения. </summary>
internal static class MaintenanceFinancialTotalsWriter
{
    internal static Guid[] GetAffectedMaintenanceIds(OpenCRMContext context)
    {
        context.ChangeTracker.DetectChanges();
        var maintenanceIds = new HashSet<Guid>();
        var details = context.ChangeTracker.Entries<MaintenanceDetail>();

        foreach (var detail in details)
        {
            if (detail.State == EntityState.Added)
            {
                maintenanceIds.Add(detail.Entity.MaintenanceId);
                continue;
            }
            if (detail.State == EntityState.Deleted)
            {
                maintenanceIds.Add(detail.Property(item => item.MaintenanceId).OriginalValue);
                continue;
            }
            if (detail.State != EntityState.Modified
                || (!detail.Property(item => item.Price).IsModified && !detail.Property(item => item.MaintenanceId).IsModified
                    && !detail.Property(item => item.Name).IsModified && !detail.Property(item => item.Position).IsModified))
            {
                continue;
            }

            maintenanceIds.Add(detail.Property(item => item.MaintenanceId).OriginalValue);
            maintenanceIds.Add(detail.Entity.MaintenanceId);
        }

        foreach (var maintenance in context.ChangeTracker.Entries<Maintenance>())
        {
            if (maintenance.State is EntityState.Added or EntityState.Deleted
                || maintenance.State == EntityState.Modified && maintenance.Properties.Any(property => property.IsModified
                    && property.Metadata.Name is nameof(Maintenance.VehicleId) or nameof(Maintenance.IsDeleted)
                        or nameof(Maintenance.Date) or nameof(Maintenance.PlannedDate) or nameof(Maintenance.CompletedDate)
                        or nameof(Maintenance.Status)
                        or nameof(Maintenance.DiscountAmount) or nameof(Maintenance.PaidAmount)
                        or nameof(Maintenance.MaterialCostAmount) or nameof(Maintenance.LaborCostAmount)
                        or nameof(Maintenance.BuyerClientId) or nameof(Maintenance.BuyerName)))
            {
                maintenanceIds.Add(maintenance.Entity.Id);
            }
        }

        return maintenanceIds.ToArray();
    }

    internal static bool HasRelevantChanges(OpenCRMContext context) =>
        MaintenanceFinancialProjectionLocks.HasRelevantChanges(context);

    internal static int Save(
        OpenCRMContext context,
        Guid[] maintenanceIds,
        Func<int> saveChanges,
        bool acceptAllChangesOnSuccess)
    {
        if (context.Database.CurrentTransaction is { } currentTransaction)
        {
            return MaintenanceFinancialTotalsWriter.SaveInTransaction(
                context, currentTransaction, maintenanceIds, saveChanges, acceptAllChangesOnSuccess);
        }

        return context.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var transaction = context.Database.BeginTransaction();
            maintenanceIds = MaintenanceFinancialProjectionLocks.LockAndExpandAsync(context, maintenanceIds, CancellationToken.None)
                .GetAwaiter().GetResult();
            var previousServices = OrganizationServiceHourlyTotalsWriter.ReadAsync(context, maintenanceIds, CancellationToken.None)
                .GetAwaiter().GetResult();
            var result = saveChanges();
            context.Database.ExecuteSql(MaintenanceFinancialTotalsWriter.GetRecomputeSql(maintenanceIds));
            OrganizationServiceHourlyTotalsWriter.ApplyAsync(context, maintenanceIds, previousServices, CancellationToken.None)
                .GetAwaiter().GetResult();
            transaction.Commit();
            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return result;
        });
    }

    internal static async Task<int> SaveAsync(
        OpenCRMContext context,
        Guid[] maintenanceIds,
        Func<CancellationToken, Task<int>> saveChanges,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } currentTransaction)
        {
            return await MaintenanceFinancialTotalsWriter.SaveInTransactionAsync(
                context, currentTransaction, maintenanceIds, saveChanges, acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async strategyCancellationToken =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(strategyCancellationToken).ConfigureAwait(false);
            maintenanceIds = await MaintenanceFinancialProjectionLocks.LockAndExpandAsync(context, maintenanceIds, strategyCancellationToken)
                .ConfigureAwait(false);
            var previousServices = await OrganizationServiceHourlyTotalsWriter.ReadAsync(context, maintenanceIds, strategyCancellationToken)
                .ConfigureAwait(false);
            var result = await saveChanges(strategyCancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlAsync(MaintenanceFinancialTotalsWriter.GetRecomputeSql(maintenanceIds), strategyCancellationToken)
                .ConfigureAwait(false);
            await OrganizationServiceHourlyTotalsWriter.ApplyAsync(context, maintenanceIds, previousServices, strategyCancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(strategyCancellationToken).ConfigureAwait(false);
            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int SaveInTransaction(
        OpenCRMContext context,
        IDbContextTransaction transaction,
        Guid[] maintenanceIds,
        Func<int> saveChanges,
        bool acceptAllChangesOnSuccess)
    {
        var savepoint = "maintenance_totals_" + Guid.NewGuid().ToString("N");
        transaction.CreateSavepoint(savepoint);
        try
        {
            maintenanceIds = MaintenanceFinancialProjectionLocks.LockAndExpandAsync(context, maintenanceIds, CancellationToken.None)
                .GetAwaiter().GetResult();
            var previousServices = OrganizationServiceHourlyTotalsWriter.ReadAsync(context, maintenanceIds, CancellationToken.None)
                .GetAwaiter().GetResult();
            var result = saveChanges();
            context.Database.ExecuteSql(MaintenanceFinancialTotalsWriter.GetRecomputeSql(maintenanceIds));
            OrganizationServiceHourlyTotalsWriter.ApplyAsync(context, maintenanceIds, previousServices, CancellationToken.None)
                .GetAwaiter().GetResult();
            transaction.ReleaseSavepoint(savepoint);
            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return result;
        }
        catch
        {
            transaction.RollbackToSavepoint(savepoint);
            throw;
        }
    }

    private static async Task<int> SaveInTransactionAsync(
        OpenCRMContext context,
        IDbContextTransaction transaction,
        Guid[] maintenanceIds,
        Func<CancellationToken, Task<int>> saveChanges,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        var savepoint = "maintenance_totals_" + Guid.NewGuid().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        try
        {
            maintenanceIds = await MaintenanceFinancialProjectionLocks.LockAndExpandAsync(context, maintenanceIds, cancellationToken)
                .ConfigureAwait(false);
            var previousServices = await OrganizationServiceHourlyTotalsWriter.ReadAsync(context, maintenanceIds, cancellationToken)
                .ConfigureAwait(false);
            var result = await saveChanges(cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlAsync(MaintenanceFinancialTotalsWriter.GetRecomputeSql(maintenanceIds), cancellationToken)
                .ConfigureAwait(false);
            await OrganizationServiceHourlyTotalsWriter.ApplyAsync(context, maintenanceIds, previousServices, cancellationToken)
                .ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return result;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static FormattableString GetRecomputeSql(Guid[] maintenanceIds) =>
        $"""
        WITH source AS (
            SELECT maintenance."Id", vehicle."OrganizationId", maintenance."CurrencyCode",
                COALESCE(maintenance."CompletedDate", maintenance."PlannedDate", maintenance."Date") AS effective_date,
                NOT maintenance."IsDeleted" AND NOT vehicle."IsDeleted" AND vehicle."OrganizationId" IS NOT NULL AS is_visible,
                maintenance."Status" <> 4 AS is_accruable_status,
                maintenance."BuyerClientId" AS client_id,
                CASE WHEN maintenance."BuyerClientId" IS NOT NULL THEN maintenance."BuyerName" END AS client_name,
                maintenance."DiscountAmount", maintenance."PaidAmount", maintenance."MaterialCostAmount", maintenance."LaborCostAmount",
                COALESCE(details.amount, 0) AS gross
            FROM "Maintenance" AS maintenance
            JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
            LEFT JOIN LATERAL (
                SELECT SUM(COALESCE(detail."Price", 0)) AS amount
                FROM "MaintenanceDetails" AS detail WHERE detail."MaintenanceId" = maintenance."Id"
            ) AS details ON TRUE
            WHERE maintenance."Id" = ANY ({maintenanceIds})
        ), calculated AS (
            SELECT source.*, is_visible AND is_accruable_status AS is_accruable,
                CASE WHEN is_accruable_status THEN GREATEST(0, gross - "DiscountAmount") ELSE 0 END AS net,
                GREATEST(LEAST(effective_date, TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                    TIMESTAMPTZ '0001-01-01 00:00:00Z') AS normalized_date
            FROM source
        )
        INSERT INTO "MaintenanceFinancialTotals"
            ("MaintenanceId", "GrossAmount", "OrganizationId", "CurrencyCode", "EffectiveDate", "UtcDay", "IsVisible", "IsAccruable", "ClientId", "ClientName",
            "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount", "MaterialCostAmount", "LaborCostAmount", "ProfitAmount", "FirstKey")
        SELECT "Id", gross, "OrganizationId", "CurrencyCode", effective_date,
            FLOOR(EXTRACT(EPOCH FROM normalized_date) / 86400)::integer + 719162, is_visible, is_accruable,
            client_id, client_name, net, "PaidAmount",
            CASE WHEN is_accruable_status THEN GREATEST(0, net - "PaidAmount") ELSE 0 END, "DiscountAmount",
            "MaterialCostAmount", "LaborCostAmount", net - "MaterialCostAmount" - "LaborCostAmount",
            LPAD((253402300799999999 - (EXTRACT(EPOCH FROM normalized_date) * 1000000)::bigint)::text, 18, '0')
                || ':' || "Id"::text
        FROM calculated WHERE TRUE
        ON CONFLICT ("MaintenanceId") DO UPDATE SET
            "GrossAmount" = EXCLUDED."GrossAmount", "OrganizationId" = EXCLUDED."OrganizationId", "CurrencyCode" = EXCLUDED."CurrencyCode",
            "EffectiveDate" = EXCLUDED."EffectiveDate", "UtcDay" = EXCLUDED."UtcDay", "IsVisible" = EXCLUDED."IsVisible",
            "IsAccruable" = EXCLUDED."IsAccruable",
            "ClientId" = EXCLUDED."ClientId", "ClientName" = EXCLUDED."ClientName", "NetAmount" = EXCLUDED."NetAmount",
            "PaidAmount" = EXCLUDED."PaidAmount", "DebtAmount" = EXCLUDED."DebtAmount", "DiscountAmount" = EXCLUDED."DiscountAmount",
            "MaterialCostAmount" = EXCLUDED."MaterialCostAmount", "LaborCostAmount" = EXCLUDED."LaborCostAmount",
            "ProfitAmount" = EXCLUDED."ProfitAmount", "FirstKey" = EXCLUDED."FirstKey"
        WHERE ("MaintenanceFinancialTotals"."GrossAmount", "MaintenanceFinancialTotals"."OrganizationId", "MaintenanceFinancialTotals"."CurrencyCode",
            "MaintenanceFinancialTotals"."EffectiveDate", "MaintenanceFinancialTotals"."UtcDay", "MaintenanceFinancialTotals"."IsVisible",
            "MaintenanceFinancialTotals"."IsAccruable",
            "MaintenanceFinancialTotals"."ClientId", "MaintenanceFinancialTotals"."ClientName", "MaintenanceFinancialTotals"."NetAmount",
            "MaintenanceFinancialTotals"."PaidAmount", "MaintenanceFinancialTotals"."DebtAmount", "MaintenanceFinancialTotals"."DiscountAmount",
            "MaintenanceFinancialTotals"."MaterialCostAmount", "MaintenanceFinancialTotals"."LaborCostAmount",
            "MaintenanceFinancialTotals"."ProfitAmount", "MaintenanceFinancialTotals"."FirstKey")
            IS DISTINCT FROM (EXCLUDED."GrossAmount", EXCLUDED."OrganizationId", EXCLUDED."CurrencyCode", EXCLUDED."EffectiveDate", EXCLUDED."UtcDay", EXCLUDED."IsVisible",
                EXCLUDED."IsAccruable",
                EXCLUDED."ClientId", EXCLUDED."ClientName", EXCLUDED."NetAmount", EXCLUDED."PaidAmount", EXCLUDED."DebtAmount", EXCLUDED."DiscountAmount",
                EXCLUDED."MaterialCostAmount", EXCLUDED."LaborCostAmount", EXCLUDED."ProfitAmount", EXCLUDED."FirstKey")
        """;
}
