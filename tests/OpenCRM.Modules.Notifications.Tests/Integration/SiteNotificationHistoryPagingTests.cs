using OpenCRM.Domain;
using OpenCRM.Modules.Notifications;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class SiteNotificationHistoryPagingTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HistoryReturnsStableDatabasePagesAndClampsPageAfterDeletion()
    {
        await using var context = fixture.CreateContext();
        var createdAt = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var notifications = Enumerable.Range(1, 21)
            .Select(index => new SiteNotification
            {
                Id = new Guid(index, 0, 0, new byte[8]),
                Title = $"История {index}",
                CreatedAt = createdAt
            })
            .ToArray();
        context.SiteNotifications.AddRange(notifications);
        context.SiteNotifications.AddRange(
            new SiteNotification { Title = "Удалено", IsDeleted = true, CreatedAt = createdAt.AddDays(1) },
            new SiteNotification { Title = "Шаблон", IsTemplate = true, CreatedAt = createdAt.AddDays(1) });
        await context.SaveChangesAsync();
        var service = new AdminSiteNotificationService(context);

        var firstPage = await service.GetHistoryPageAsync(1, 20, default);
        var lastPage = await service.GetHistoryPageAsync(2, 20, default);

        Assert.Equal(21, firstPage.TotalCount);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Single(lastPage.Items);
        Assert.Equal(2, lastPage.Page);
        Assert.Equal(notifications.OrderByDescending(item => item.Id).Select(item => item.Id),
            firstPage.Items.Concat(lastPage.Items).Select(item => item.Id));

        Assert.True(await service.TryDeleteAsync(lastPage.Items[0].Id, default));
        var clampedPage = await service.GetHistoryPageAsync(2, 20, default);

        Assert.Equal(20, clampedPage.TotalCount);
        Assert.Equal(1, clampedPage.Page);
        Assert.Equal(20, clampedPage.Items.Count);
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(-5, 500, 100)]
    [InlineData(1, 10, 10)]
    [InlineData(1, 500, 100)]
    public async Task HistoryClampsPageAndPageSize(int requestedPage, int requestedPageSize, int expectedPageSize)
    {
        await using var context = fixture.CreateContext();
        context.SiteNotifications.Add(new SiteNotification { Title = "Одна запись" });
        await context.SaveChangesAsync();
        var service = new AdminSiteNotificationService(context);

        var result = await service.GetHistoryPageAsync(requestedPage, requestedPageSize, default);

        Assert.Equal(1, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
    }
}
