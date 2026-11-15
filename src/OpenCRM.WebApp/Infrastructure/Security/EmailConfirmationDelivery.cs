using Microsoft.Extensions.Caching.Memory;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Ограничивает повторную отправку писем подтверждения для аккаунта. </summary>
public sealed class EmailConfirmationDelivery(IMemoryCache cache, TimeProvider timeProvider)
{
    private static readonly TimeSpan s_cooldown = TimeSpan.FromSeconds(60);
    private readonly object _syncRoot = new();

    /// <summary> Возвращает время до следующей допустимой отправки. </summary>
    public int GetRetryAfterSeconds(string userId)
    {
        lock (this._syncRoot)
        {
            if (!cache.TryGetValue(EmailConfirmationDelivery.GetCacheKey(userId), out Entry? entry) || entry == null)
            {
                return 0;
            }

            if (entry.IsSending)
            {
                return (int)EmailConfirmationDelivery.s_cooldown.TotalSeconds;
            }

            var remaining = entry.CooldownUntil - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                cache.Remove(EmailConfirmationDelivery.GetCacheKey(userId));

                return 0;
            }

            return (int)Math.Ceiling(remaining.TotalSeconds);
        }
    }

    /// <summary> Отправляет письмо, если для аккаунта нет активной отправки или паузы. </summary>
    public async Task<bool> TrySendAsync(string userId, Func<Task> send)
    {
        var cacheKey = EmailConfirmationDelivery.GetCacheKey(userId);
        var reservation = new Entry(true, DateTimeOffset.MinValue);
        lock (this._syncRoot)
        {
            if (cache.TryGetValue(cacheKey, out Entry? entry) && entry != null)
            {
                if (entry.IsSending || entry.CooldownUntil > timeProvider.GetUtcNow())
                {
                    return false;
                }

                cache.Remove(cacheKey);
            }

            cache.Set(cacheKey, reservation);
        }

        try
        {
            await send();
        }
        catch
        {
            lock (this._syncRoot)
            {
                if (cache.TryGetValue(cacheKey, out Entry? entry) && ReferenceEquals(entry, reservation))
                {
                    cache.Remove(cacheKey);
                }
            }

            throw;
        }

        lock (this._syncRoot)
        {
            if (cache.TryGetValue(cacheKey, out Entry? entry) && ReferenceEquals(entry, reservation))
            {
                var cooldownUntil = timeProvider.GetUtcNow().Add(EmailConfirmationDelivery.s_cooldown);
                cache.Set(cacheKey, new Entry(false, cooldownUntil), new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = EmailConfirmationDelivery.s_cooldown
                });
            }
        }

        return true;
    }

    private static string GetCacheKey(string userId) => $"email-confirmation-delivery:{userId}";

    private sealed record Entry(bool IsSending, DateTimeOffset CooldownUntil);
}
