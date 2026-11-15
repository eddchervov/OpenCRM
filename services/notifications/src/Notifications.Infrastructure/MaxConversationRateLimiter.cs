using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Notifications.Infrastructure;

/// <summary>Ограничивает частоту MAX между всеми экземплярами сервиса блокировками PostgreSQL.</summary>
public sealed class MaxConversationRateLimiter(
    NotificationDbContext context,
    TimeProvider timeProvider) : IMaxConversationRateLimiter, IAsyncDisposable
{
    private static readonly TimeSpan GlobalInterval = TimeSpan.FromMilliseconds(34);
    private static readonly TimeSpan ConversationInterval = TimeSpan.FromMilliseconds(500);
    private static readonly long GlobalLockId = CreateLockId("max:global");

    /// <summary>Резервирует интервал для диалога между всеми экземплярами сервиса.</summary>
    public async Task<IAsyncDisposable> AcquireConversationAsync(string key, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var conversationLockId = CreateLockId($"max:conversation:{key}");
        var conversationLockAcquired = false;
        try
        {
            await ExecuteLockAsync(connection, "pg_advisory_lock", conversationLockId, cancellationToken);
            conversationLockAcquired = true;

            return new ConversationLease(connection, conversationLockId, timeProvider, timeProvider.GetTimestamp());
        }
        catch
        {
            if (conversationLockAcquired)
            {
                await ExecuteLockAsync(connection, "pg_advisory_unlock", conversationLockId, CancellationToken.None);
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Резервирует один запрос к MAX API в общем лимите всех экземпляров.</summary>
    public async Task WaitForGlobalRequestSlotAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteLockAsync(connection, "pg_advisory_lock", GlobalLockId, cancellationToken);
        try
        {
            await Task.Delay(GlobalInterval, timeProvider, cancellationToken);
        }
        finally
        {
            await ExecuteLockAsync(connection, "pg_advisory_unlock", GlobalLockId, CancellationToken.None);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task ExecuteLockAsync(
        NpgsqlConnection connection,
        string operation,
        long lockId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {operation}(@lock_id)", connection);
        command.Parameters.AddWithValue("lock_id", lockId);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static long CreateLockId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    private sealed class ConversationLease(
        NpgsqlConnection connection,
        long lockId,
        TimeProvider timeProvider,
        long acquiredTimestamp) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            var remaining = ConversationInterval - timeProvider.GetElapsedTime(acquiredTimestamp);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, timeProvider, CancellationToken.None);
            }

            try
            {
                await ExecuteLockAsync(connection, "pg_advisory_unlock", lockId, CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
