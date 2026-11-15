using Npgsql;

namespace OpenCRM.Modules.Organizations.Export;

/// <summary> Удерживает межпроцессный слот до завершения подготовки или скачивания. </summary>
internal sealed class OrganizationDataExportLock : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private readonly long _key;

    private OrganizationDataExportLock(NpgsqlConnection connection, long key)
    {
        this._connection = connection;
        this._key = key;
    }

    internal static async Task<OrganizationDataExportLock?> TryAcquireAsync(
        string connectionString, long key, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            if (Equals(await command.ExecuteScalarAsync(cancellationToken), true))
            {
                return new OrganizationDataExportLock(connection, key);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref this._connection, null);
        if (connection == null)
        {
            return;
        }

        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection) { CommandTimeout = 5 };
            command.Parameters.AddWithValue("key", this._key);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
