using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Выполняет операции обслуживания в транзакции. </summary>
internal sealed class MaintenanceTransaction(OpenCRMContext context)
{
    /// <summary> Блокирует организацию для последовательного выполнения операций. </summary>
    internal async Task LockOrganizationAsync(Guid? organizationId)
    {
        if (organizationId.HasValue)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId.Value} FOR NO KEY UPDATE""");
        }
    }

    internal Task LockUserAsync(string userId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT 1 FROM identity."AspNetUsers" WHERE "Id" = {userId} FOR NO KEY UPDATE""");

    /// <summary> Выполняет действие с проверкой успешности. </summary>
    internal Task ExecuteAsync(Func<Task> action, Func<Task<bool>> verifySucceeded) =>
        this.ExecuteAsync(async () =>
        {
            await action();

            return true;
        }, verifySucceeded);

    /// <summary> Выполняет действие с результатом и проверкой успешности. </summary>
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
