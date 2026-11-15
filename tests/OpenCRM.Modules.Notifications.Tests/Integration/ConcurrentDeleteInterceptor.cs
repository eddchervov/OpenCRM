using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

internal sealed class ConcurrentDeleteInterceptor(string table, Func<Task> delete) : DbCommandInterceptor
{
    public bool HasDeleted { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasDeleted && command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
        {
            this.HasDeleted = true;
            await delete();
        }

        return result;
    }
}
