using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleDocumentHistoryServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task TrackDocumentGeneratedPersistsPersonalAndOrganizationEventsAndTruncatesLongDescriptions()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "documents@example.test" };
        var deletedUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName ="deleted-account",
            IsDeleted = true,
            OriginalEmail = "former@example.test"
        };
        var usernameOnlyUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName ="username-only" };
        var anonymousUser = new ApplicationUser { Id = Guid.NewGuid().ToString() };
        var organization = new Organization { Name = "Организация документов", OwnerUser = user, OwnerUserId = user.Id };
        var personalVehicle = new Vehicle
        {
            UserId = user.Id,
            User = user,
            ManualBrandName = "Toyota",
            ManualModelName = "Camry",
            DateTimeCreation = DateTime.UtcNow
        };
        var organizationVehicle = new Vehicle
        {
            UserId = user.Id,
            User = user,
            OrganizationId = organization.Id,
            Organization = organization,
            ManualBrandName = "Lada",
            ManualModelName = "Granta",
            DateTimeCreation = DateTime.UtcNow
        };
        var deletedUserVehicle = new Vehicle
        {
            UserId = deletedUser.Id,
            User = deletedUser,
            ManualBrandName = "ВАЗ",
            ManualModelName = "2107",
            DateTimeCreation = DateTime.UtcNow
        };
        var usernameOnlyVehicle = new Vehicle
        {
            UserId = usernameOnlyUser.Id,
            User = usernameOnlyUser,
            ManualBrandName = "ГАЗ",
            ManualModelName = "Газель",
            DateTimeCreation = DateTime.UtcNow
        };
        var anonymousVehicle = new Vehicle
        {
            UserId = anonymousUser.Id,
            User = anonymousUser,
            ManualBrandName = "УАЗ",
            ManualModelName = "Патриот",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Users.AddRange(user, deletedUser, usernameOnlyUser, anonymousUser);
        context.Organizations.Add(organization);
        context.Vehicles.AddRange(personalVehicle, organizationVehicle, deletedUserVehicle, usernameOnlyVehicle, anonymousVehicle);
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(user.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var clock = new Mock<ISystemClock>();
        var recordedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        clock.SetupGet(systemClock => systemClock.UtcNow).Returns(recordedAt);
        IVehicleDocumentHistoryService service = new VehicleHistoryWriter(
            context,
            clock.Object,
            new VehicleRepository(context),
            access.Object);

        await service.TrackDocumentGenerated(user.Id, null, personalVehicle.Id, "Личный документ");
        await service.TrackDocumentGenerated(user.Id, organization.Id, organizationVehicle.Id, "Документ организации", string.Empty);
        var longDescription = new string('Д', 1001);
        await service.TrackDocumentGenerated(user.Id, organization.Id, organizationVehicle.Id, "Большой документ", longDescription);
        await service.TrackDocumentGenerated(user.Id, null, personalVehicle.Id, "Короткое описание", "PDF");
        await service.TrackDocumentGenerated(deletedUser.Id, null, deletedUserVehicle.Id, "Удаленный пользователь");
        await service.TrackDocumentGenerated(usernameOnlyUser.Id, null, usernameOnlyVehicle.Id, "Имя пользователя");
        await service.TrackDocumentGenerated(anonymousUser.Id, null, anonymousVehicle.Id, "Без имени");

        var events = await context.VehicleHistoryEvents.AsNoTracking()
            .OrderBy(item => item.Title)
            .ToListAsync();

        Assert.Equal(7, events.Count);
        Assert.All(events, item =>
        {
            Assert.Equal("document_generated", item.EventType);
            Assert.Equal(recordedAt, item.CreatedAt);
            Assert.Equal("vehicle", item.RelatedEntityType);
        });
        var personalEvent = Assert.Single(events, item => item.Title == "Личный документ");
        Assert.Null(personalEvent.OrganizationId);
        Assert.Equal(personalVehicle.Id, personalEvent.VehicleId);
        Assert.Null(personalEvent.Description);
        Assert.Equal(personalVehicle.Id, personalEvent.RelatedEntityId);

        var organizationEvent = Assert.Single(events, item => item.Title == "Документ организации");
        Assert.Equal(organization.Id, organizationEvent.OrganizationId);
        Assert.Equal(organizationVehicle.Id, organizationEvent.VehicleId);
        Assert.Equal(string.Empty, organizationEvent.Description);

        var longEvent = Assert.Single(events, item => item.Title == "Большой документ");
        Assert.Equal(1000, longEvent.Description?.Length);
        Assert.Equal(new string('Д', 1000), longEvent.Description);
        Assert.Equal(organization.Id, longEvent.OrganizationId);
        access.Verify(service => service.EnsureWorkspaceCanReadAsync(user.Id, organization.Id, OrganizationPermissionCodes.VehiclesView), Times.Exactly(2));

        var shortDescriptionEvent = Assert.Single(events, item => item.Title == "Короткое описание");
        Assert.Equal("PDF", shortDescriptionEvent.Description);
        Assert.Equal(user.Email, shortDescriptionEvent.ActorDisplayName);
        Assert.Equal("former@example.test", Assert.Single(events, item => item.Title == "Удаленный пользователь").ActorDisplayName);
        Assert.Equal("username-only", Assert.Single(events, item => item.Title == "Имя пользователя").ActorDisplayName);
        Assert.Equal("Пользователь", Assert.Single(events, item => item.Title == "Без имени").ActorDisplayName);

        var savedCount = events.Count;
        await Assert.ThrowsAsync<Exception>(() => service.TrackDocumentGenerated(user.Id, null, Guid.NewGuid(), "Не найдено"));
        Assert.Equal(savedCount, await context.VehicleHistoryEvents.CountAsync());

        var writer = Assert.IsType<VehicleHistoryWriter>(service);
        await Assert.ThrowsAsync<DbUpdateException>(() => writer.AddAsync(
            "missing-user",
            null,
            personalVehicle.Id,
            "direct_event",
            "Пользователь отсутствует",
            null,
            null,
            null));
        Assert.Equal(savedCount, await context.VehicleHistoryEvents.CountAsync());
    }
}
