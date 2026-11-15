using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Выполняет команды инвентаризации с проверкой результата. </summary>
internal sealed class InventoryCommandTransaction(OpenCRMContext context)
{
    /// <summary> Выполняет действие и проверяет успешность, при необходимости повторяя. </summary>
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
}
