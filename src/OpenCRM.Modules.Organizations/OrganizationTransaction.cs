using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Organizations;

/// <summary> Выполнение операций с организациями в транзакциях. </summary>
internal sealed class OrganizationTransaction(OpenCRMContext context)
{
    /// <summary> Выполняет действие в транзакции с новым идентификатором события. </summary>
    internal Task ExecuteAuditedAsync(Func<Guid, Task> action)
    {
        var eventId = Guid.NewGuid();

        return this.ExecuteAsync(
            () => action(eventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(auditEvent => auditEvent.Id == eventId));
    }

    /// <summary> Выполняет действие в транзакции. </summary>
    internal Task ExecuteAsync(Func<Task> action) =>
        this.ExecuteAsync(action, () => Task.FromResult(false));

    /// <summary> Выполняет действие в транзакции с проверкой успеха. </summary>
    internal Task ExecuteAsync(Func<Task> action, Func<Task<bool>> verifySucceeded) =>
        this.ExecuteAsync(async () =>
        {
            await action();

            return true;
        }, verifySucceeded);

    /// <summary> Выполняет действие с результатом в транзакции и проверкой успеха. </summary>
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

    /// <summary> Блокирует организацию для конкурентных операций. </summary>
    internal Task LockOrganizationAsync(Guid organizationId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
}
