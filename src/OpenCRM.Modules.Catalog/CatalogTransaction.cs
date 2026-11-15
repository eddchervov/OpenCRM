using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Catalog;

/// <summary> Обёртка транзакций для операций каталога. </summary>
internal sealed class CatalogTransaction(OpenCRMContext context)
{
    /// <summary> Выполняет действие в транзакции с записью аудита. </summary>
    internal Task<T> ExecuteAuditedAsync<T>(Func<Guid, Task<T>> action)
    {
        var eventId = Guid.NewGuid();

        return this.ExecuteAsync(() => action(eventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.Id == eventId));
    }

    /// <summary> Выполняет действие в транзакции с проверкой успеха. </summary>
    internal async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Task<bool>> verifySucceeded)
    {
        if (context.Database.CurrentTransaction != null)
        {
            return await action();
        }

        var isRetry = false;
        T result = default!;
        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
            {
                if (isRetry)
                {
                    context.ChangeTracker.Clear();
                }

                isRetry = true;
                result = await action();
            }, verifySucceeded);
        }
        catch
        {
            context.ChangeTracker.Clear();

            throw;
        }

        return result;
    }

    /// <summary> Блокирует строки организации для избежания конкуренции. </summary>
    internal Task LockOrganizationAsync(Guid organizationId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
}
