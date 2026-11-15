using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Common;

namespace OpenCRM.Infrastructure.Common;

/// <summary> Единица работы с транзакциями поверх контекста базы данных. </summary>
public class ApplicationUnitOfWork(OpenCRMContext opencrmContext) : IApplicationUnitOfWork
{
    /// <summary> Выполняет действие внутри транзакции базы данных. </summary>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await opencrmContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await opencrmContext.Database.BeginTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                // После отката повторная попытка не должна сохранять сущности предыдущей попытки.
                opencrmContext.ChangeTracker.Clear();

                throw;
            }
        });
    }
}
