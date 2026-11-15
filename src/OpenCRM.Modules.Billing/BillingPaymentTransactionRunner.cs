using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Исполнитель транзакций для операций биллинга. </summary>
internal sealed class BillingPaymentTransactionRunner(OpenCRMContext context)
{
    /// <summary> Выполняет операцию в транзакции базы данных. </summary>
    public async Task<SubscriptionResult> ExecuteAsync(Func<IDbContextTransaction, Task<SubscriptionResult>> execute)
    {
        if (context.Database.CurrentTransaction != null)
        {
            throw new InvalidOperationException("Активацию подписки нельзя выполнять внутри внешней транзакции.");
        }

        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Активацию подписки нужно выполнять в отдельной единице работы без ожидающих изменений.");
        }

        var strategy = context.Database.CreateExecutionStrategy();
        var isRetry = false;

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                if (isRetry)
                {
                    context.ChangeTracker.Clear();
                }

                isRetry = true;
                using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                return await execute(transaction);
            });
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
