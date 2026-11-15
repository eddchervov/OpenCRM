using OpenCRM.Infrastructure.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class AdminDashboardCacheTests
{
    [Fact]
    public async Task ReusesValueForSamePeriodPermissionAndUtcDate()
    {
        var cache = new AdminDashboardCache(new TestTimeProvider());
        var expected = new AdminDashboardDto();
        var factoryCalls = 0;

        var first = await cache.GetOrCreateAsync(7, false, new DateTime(2026, 10, 16), () =>
        {
            factoryCalls++;

            return Task.FromResult(expected);
        });
        var second = await cache.GetOrCreateAsync(7, false, new DateTime(2026, 10, 16), () =>
        {
            factoryCalls++;

            return Task.FromResult(new AdminDashboardDto());
        });

        Assert.Same(expected, first);
        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task SeparatesPeriodPermissionAndUtcDate()
    {
        var cache = new AdminDashboardCache(new TestTimeProvider());
        var factoryCalls = 0;

        Task<AdminDashboardDto> CreateValue()
        {
            factoryCalls++;

            return Task.FromResult(new AdminDashboardDto());
        }

        await cache.GetOrCreateAsync(7, false, new DateTime(2026, 10, 16), CreateValue);
        await cache.GetOrCreateAsync(30, false, new DateTime(2026, 10, 16), CreateValue);
        await cache.GetOrCreateAsync(7, true, new DateTime(2026, 10, 16), CreateValue);
        await cache.GetOrCreateAsync(7, false, new DateTime(2026, 10, 17), CreateValue);

        Assert.Equal(4, factoryCalls);
    }

    [Fact]
    public async Task RebuildsValueAfterTtlExpires()
    {
        var timeProvider = new TestTimeProvider();
        var cache = new AdminDashboardCache(timeProvider);
        var factoryCalls = 0;

        Task<AdminDashboardDto> CreateValue()
        {
            factoryCalls++;

            return Task.FromResult(new AdminDashboardDto { GeneratedAt = timeProvider.GetUtcNow().UtcDateTime });
        }

        var first = await cache.GetOrCreateAsync(1, false, timeProvider.GetUtcNow().UtcDateTime.Date, CreateValue);
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        var second = await cache.GetOrCreateAsync(1, false, timeProvider.GetUtcNow().UtcDateTime.Date, CreateValue);

        Assert.NotSame(first, second);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneCacheFill()
    {
        var cache = new AdminDashboardCache(new TestTimeProvider());
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<AdminDashboardDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;

        Task<AdminDashboardDto> CreateValue()
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();

            return releaseFactory.Task;
        }

        var requests = Enumerable.Range(0, 12)
            .Select(_ => cache.GetOrCreateAsync(1, false, new DateTime(2026, 10, 16), CreateValue))
            .ToArray();
        await factoryStarted.Task;
        var expected = new AdminDashboardDto();
        releaseFactory.SetResult(expected);
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, factoryCalls);
        Assert.All(results, result => Assert.Same(expected, result));
    }

    [Fact]
    public async Task DoesNotCacheFactoryFailures()
    {
        var cache = new AdminDashboardCache(new TestTimeProvider());
        var factoryCalls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync(1, false,
            new DateTime(2026, 10, 16), () =>
            {
                factoryCalls++;

                throw new InvalidOperationException();
            }));
        await cache.GetOrCreateAsync(1, false, new DateTime(2026, 10, 16), () =>
        {
            factoryCalls++;

            return Task.FromResult(new AdminDashboardDto());
        });

        Assert.Equal(2, factoryCalls);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this._now;

        public void Advance(TimeSpan duration) => this._now += duration;
    }
}
