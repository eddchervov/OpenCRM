using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class AsyncDisposalTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
    : RelationalTransactionFactory(dependencies)
{
    public bool HasDisposedTransaction { get; private set; }

    public override RelationalTransaction Create(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned)
    {
        return new AsyncDisposalTransaction(connection, transaction, transactionId, logger, transactionOwned,
            this.Dependencies.SqlGenerationHelper, this);
    }

    private sealed class AsyncDisposalTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper,
        AsyncDisposalTransactionFactory owner)
        : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        public override async ValueTask DisposeAsync()
        {
            // Асинхронное освобождение ресурса не должно теряться при завершении обработчика.
            await Task.Delay(10);
            await base.DisposeAsync();
            owner.HasDisposedTransaction = true;
        }
    }
}
