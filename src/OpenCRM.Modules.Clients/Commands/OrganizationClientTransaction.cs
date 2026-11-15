using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Обёртка транзакций для операций с клиентами. </summary>
internal sealed class OrganizationClientTransaction(OpenCRMContext context)
{
    /// <summary> Выполняет действие в транзакции с проверкой успеха. </summary>
    internal Task ExecuteAsync(Func<Task> action, Func<Task<bool>> verifySucceeded) =>
        this.ExecuteAsync(async () =>
        {
            await action();

            return true;
        }, verifySucceeded);

    /// <summary> Выполняет действие с результатом в транзакции. </summary>
    internal async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Task<bool>> verifySucceeded)
    {
        if (context.Database.CurrentTransaction != null)
        {
            return await action();
        }

        var isRetry = false;
        T result = default!;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            result = await action();
        }, verifySucceeded);

        return result;
    }

    /// <summary> Блокирует строки организации для избежания конкуренции. </summary>
    internal Task LockOrganizationAsync(Guid organizationId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
}
