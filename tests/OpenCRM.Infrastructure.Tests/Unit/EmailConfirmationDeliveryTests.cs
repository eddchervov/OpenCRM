using Microsoft.Extensions.Caching.Memory;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class EmailConfirmationDeliveryTests
{
    [Fact]
    public async Task RejectsSequentialSendDuringCooldown()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var delivery = new EmailConfirmationDelivery(memoryCache, new TestTimeProvider());
        var sendCount = 0;

        var first = await delivery.TrySendAsync("user-1", () =>
        {
            sendCount++;

            return Task.CompletedTask;
        });
        var second = await delivery.TrySendAsync("user-1", () =>
        {
            sendCount++;

            return Task.CompletedTask;
        });

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, sendCount);
        Assert.Equal(60, delivery.GetRetryAfterSeconds("user-1"));
    }

    [Fact]
    public async Task RejectsConcurrentSendWhileFirstCallbackIsRunning()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var delivery = new EmailConfirmationDelivery(memoryCache, new TestTimeProvider());
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = delivery.TrySendAsync("user-1", async () =>
        {
            sendStarted.SetResult();
            await completeSend.Task;
        });

        await sendStarted.Task;
        var second = await delivery.TrySendAsync("user-1", () => Task.CompletedTask);
        completeSend.SetResult();
        var first = await firstTask;

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task AllowsSendAfterCooldownExpiresInTimeProvider()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var timeProvider = new TestTimeProvider();
        var delivery = new EmailConfirmationDelivery(memoryCache, timeProvider);

        Assert.True(await delivery.TrySendAsync("user-1", () => Task.CompletedTask));
        timeProvider.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(0, delivery.GetRetryAfterSeconds("user-1"));
        Assert.True(await delivery.TrySendAsync("user-1", () => Task.CompletedTask));
    }

    [Fact]
    public async Task ReleasesReservationWhenSendFails()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var delivery = new EmailConfirmationDelivery(memoryCache, new TestTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.TrySendAsync("user-1", () =>
            throw new InvalidOperationException()));

        Assert.Equal(0, delivery.GetRetryAfterSeconds("user-1"));
        Assert.True(await delivery.TrySendAsync("user-1", () => Task.CompletedTask));
    }

    [Fact]
    public async Task TracksCooldownSeparatelyForEachAccount()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var delivery = new EmailConfirmationDelivery(memoryCache, new TestTimeProvider());

        Assert.True(await delivery.TrySendAsync("user-1", () => Task.CompletedTask));
        Assert.True(await delivery.TrySendAsync("user-2", () => Task.CompletedTask));
        Assert.Equal(60, delivery.GetRetryAfterSeconds("user-1"));
        Assert.Equal(60, delivery.GetRetryAfterSeconds("user-2"));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this._now;

        public void Advance(TimeSpan duration) => this._now += duration;
    }
}
