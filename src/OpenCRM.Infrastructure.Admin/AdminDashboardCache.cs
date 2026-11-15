using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Кеширует сводки дашборда на короткий срок. </summary>
public sealed class AdminDashboardCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan s_cacheDuration = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly Dictionary<DashboardCacheKey, CacheEntry> _entries = [];
    private readonly Dictionary<DashboardCacheKey, TaskCompletionSource<AdminDashboardDto>> _inFlight = [];

    /// <summary> Возвращает кешированную сводку или заполняет кеш одним запросом. </summary>
    public async Task<AdminDashboardDto> GetOrCreateAsync(
        int periodDays,
        bool canViewBilling,
        DateTime utcDate,
        Func<Task<AdminDashboardDto>> factory)
    {
        var key = new DashboardCacheKey(periodDays, canViewBilling, DateOnly.FromDateTime(utcDate));
        TaskCompletionSource<AdminDashboardDto> completionSource;
        Task<AdminDashboardDto> resultTask;
        var isOwner = false;

        lock (this._sync)
        {
            var now = timeProvider.GetUtcNow();

            var expiredKeys = this._entries
                .Where(pair => pair.Value.ExpiresAt <= now)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var expiredKey in expiredKeys)
            {
                this._entries.Remove(expiredKey);
            }

            if (this._entries.TryGetValue(key, out var entry))
            {
                return entry.Value;
            }

            if (this._inFlight.TryGetValue(key, out var inFlight))
            {
                completionSource = inFlight;
            }
            else
            {
                completionSource = new TaskCompletionSource<AdminDashboardDto>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                this._inFlight.Add(key, completionSource);
                isOwner = true;
            }

            resultTask = completionSource.Task;
        }

        if (!isOwner)
        {
            return await resultTask;
        }

        try
        {
            var value = await factory();
            lock (this._sync)
            {
                this._entries[key] = new CacheEntry(
                    value,
                    timeProvider.GetUtcNow() + AdminDashboardCache.s_cacheDuration);
                this._inFlight.Remove(key);
            }

            completionSource.SetResult(value);

            return value;
        }
        catch (Exception exception)
        {
            lock (this._sync)
            {
                this._inFlight.Remove(key);
            }

            completionSource.SetException(exception);

            return await resultTask;
        }
    }

    private sealed record DashboardCacheKey(int PeriodDays, bool CanViewBilling, DateOnly UtcDate);

    private sealed record CacheEntry(AdminDashboardDto Value, DateTimeOffset ExpiresAt);
}
