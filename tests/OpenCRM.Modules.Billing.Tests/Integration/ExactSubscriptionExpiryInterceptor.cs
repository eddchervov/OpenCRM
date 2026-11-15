using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenCRM.Modules.Billing.Tests.Integration;

internal sealed class ExactSubscriptionExpiryInterceptor(DateTime expiry) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains("OrganizationSubscriptions", StringComparison.Ordinal))
        {
            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }

        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is DateTime)
            {
                parameter.Value = expiry;
            }
        }

        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }
}
