using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

internal sealed class OrganizationCommitFailureInterceptor(
    bool failAfterCommit,
    Func<Task>? afterCommit = null) : DbTransactionInterceptor
{
    private bool _armed;
    private bool _hasFailed;

    internal int CommitAttempts { get; private set; }
    internal int Failures { get; private set; }

    internal void Arm() => this._armed = true;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (this._armed)
        {
            this.CommitAttempts++;
            if (!failAfterCommit)
            {
                this.FailOnce();
            }
        }

        return ValueTask.FromResult(result);
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (this._armed && failAfterCommit && !this._hasFailed)
        {
            if (afterCommit != null)
            {
                await afterCommit();
            }

            this.FailOnce();
        }
    }

    private void FailOnce()
    {
        if (this._hasFailed)
        {
            return;
        }

        this._hasFailed = true;
        this.Failures++;
        throw new PostgresException("Временный сбой фиксации команды организации.", "ERROR", "ERROR", "40001");
    }
}
