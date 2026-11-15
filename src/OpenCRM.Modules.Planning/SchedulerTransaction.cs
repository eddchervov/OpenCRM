using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Planning;

/// <summary> Оборачивает операции планировщика в транзакции с аудитом. </summary>
internal sealed class SchedulerTransaction(OpenCRMContext context)
{
    /// <summary> Выполняет действие в транзакции с генерацией идентификатора события. </summary>
    internal Task<T> ExecuteAuditedAsync<T>(Func<Guid, Task<T>> action)
    {
        var eventId = Guid.NewGuid();

        return this.ExecuteAsync(() => action(eventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.Id == eventId));
    }

    /// <summary> Выполняет действие без результата в транзакции с аудитом. </summary>
    internal Task ExecuteAuditedAsync(Func<Guid, Task> action) =>
        this.ExecuteAuditedAsync(async eventId =>
        {
            await action(eventId);

            return true;
        });

    /// <summary> Выполняет действие в транзакции с проверкой успешности. </summary>
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

    /// <summary> Блокирует организацию для последовательных изменений. </summary>
    internal Task LockOrganizationAsync(Guid organizationId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
}
