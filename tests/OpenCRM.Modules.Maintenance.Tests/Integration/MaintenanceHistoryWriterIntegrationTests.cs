using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceHistoryWriterIntegrationTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task UpdateHistoryRecordsStatusFieldsAndDetailChangesThenSummaryForNoopUpdate()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
        var (user, maintenance) = await CreateMaintenanceAsync(context);
        var writer = new MaintenanceHistoryWriter(CreateClock(now), context);
        var previous = MaintenanceSnapshot.From(maintenance);

        maintenance.Status = MaintenanceStatus.InProgress;
        maintenance.Comment = "Новый комментарий";
        maintenance.Details.Single().Name = "Обновлённая полировка";
        await writer.AddUpdateHistoryEventsAsync(user.Id, maintenance.Vehicle.OrganizationId, maintenance, previous);

        var history = await context.MaintenanceHistoryEvents
            .Where(item => item.MaintenanceId == maintenance.Id)
            .OrderBy(item => item.EventType)
            .ToListAsync();
        Assert.Collection(history,
            status =>
            {
                Assert.Equal(MaintenanceHistoryEventCodes.EventDetailsChanged, status.EventType);
                Assert.Equal("Изменены услуги и работы", status.Title);
                Assert.Contains("название: Полировка -> Обновлённая полировка", status.Description);
                Assert.Equal("writer@example.test", status.ActorDisplayName);
                Assert.Equal(now, status.CreatedAt);
            },
            fields =>
            {
                Assert.Equal(MaintenanceHistoryEventCodes.EventFieldsChanged, fields.EventType);
                Assert.Contains("Комментарий: не указано -> Новый комментарий", fields.Description);
            },
            state =>
            {
                Assert.Equal(MaintenanceHistoryEventCodes.EventStatusChanged, state.EventType);
                Assert.Equal("Запланировано -> В работе", state.Description);
            });

        var unchanged = MaintenanceSnapshot.From(maintenance);
        await writer.AddUpdateHistoryEventsAsync(user.Id, maintenance.Vehicle.OrganizationId, maintenance, unchanged);
        var summary = await context.MaintenanceHistoryEvents.SingleAsync(item =>
            item.MaintenanceId == maintenance.Id && item.EventType == MaintenanceHistoryEventCodes.EventUpdated);
        Assert.Equal("Обслуживание обновлено", summary.Title);
        Assert.Contains("Состояние: В работе", summary.Description);
        Assert.Equal("writer@example.test", summary.ActorDisplayName);
    }

    [Fact]
    public async Task HistoryWriterResolvesDeletedAndMissingActorsAndComparesLinkedDefects()
    {
        await using var context = fixture.CreateContext();
        var now = DateTime.UtcNow;
        var (user, maintenance) = await CreateMaintenanceAsync(context);
        var deletedUser = new ApplicationUser
        {
            UserName = $"deleted-{Guid.NewGuid():N}",
            Email = "old-address@example.test",
            OriginalEmail = "original-address@example.test",
            IsDeleted = true
        };
        context.Users.Add(deletedUser);
        await context.SaveChangesAsync();

        var writer = new MaintenanceHistoryWriter(CreateClock(now), context);

        Assert.Equal("original-address@example.test", await writer.ResolveActorDisplayNameAsync(deletedUser.Id));
        Assert.Equal("Неизвестный пользователь", await writer.ResolveActorDisplayNameAsync("missing-user"));
        Assert.False(await writer.HasLinkedDefectChangesAsync(null, maintenance.Id, [Guid.NewGuid()]));
        Assert.False(await writer.HasLinkedDefectChangesAsync(maintenance.Vehicle.OrganizationId, maintenance.Id, null));
        Assert.False(await writer.HasLinkedDefectChangesAsync(maintenance.Vehicle.OrganizationId, maintenance.Id, []));
        Assert.True(await writer.HasLinkedDefectChangesAsync(
            maintenance.Vehicle.OrganizationId, maintenance.Id, [Guid.NewGuid(), Guid.NewGuid()]));

        await writer.AddHistoryEventAsync(
            deletedUser.Id,
            maintenance.Vehicle.OrganizationId,
            maintenance.VehicleId,
            maintenance.Id,
            MaintenanceHistoryEventCodes.EventUpdated,
            "Событие от удалённого пользователя");

        var history = await context.MaintenanceHistoryEvents.SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal("original-address@example.test", history.ActorDisplayName);
        Assert.Equal(now, history.CreatedAt);
        Assert.Equal(user.Id, maintenance.UserId);
    }

    private static async Task<(ApplicationUser User, MaintenanceEntity Maintenance)> CreateMaintenanceAsync(
        OpenCRMContext context)
    {
        var user = new ApplicationUser
        {
            UserName = $"writer-{Guid.NewGuid():N}",
            Email = "writer@example.test"
        };
        var organization = new Organization { Name = "История", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = new DateTime(2026, 10, 11),
            Details = [new MaintenanceDetail { Name = "Полировка" }]
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        return (user, maintenance);
    }

    private static ISystemClock CreateClock(DateTime now)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);

        return clock.Object;
    }
}
