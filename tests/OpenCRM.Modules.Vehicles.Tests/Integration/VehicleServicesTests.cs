using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleServicesTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task CommandsQueriesAndHistoryPreservePersonalAndOrganizationLifecycle()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        context.Organizations.Add(organization);
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(value => value.GetVehicleLimitAsync(user.Id, organization.Id, 3)).ReturnsAsync(2);
        var features = new Mock<IFeatureAccessService>();
        features.Setup(value => value.GetIntValueAsync(user.Id, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(3);
        var repository = new VehicleRepository(context);
        var limits = new VehicleLimitQueryService(repository, Mock.Of<IPersonalSubscriptionQueryService>(), access.Object, features.Object,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
        var history = new VehicleHistoryWriter(context, clock.Object, repository, access.Object);
        var commands = new VehicleCommandService(repository, new ModelRepository(context), clock.Object,
            new VehicleCatalogSuggestionRepository(context), context, access.Object, history, limits);
        var photos = new Mock<IVehiclePhotoRepository>();
        photos.Setup(value => value.GetByVehicleIdsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());
        var queries = new VehicleQueryService(repository, photos.Object, context, access.Object, Mock.Of<ISystemClock>());
        var vehicleId = await commands.Create(user.Id, new CreateVehicleDto { RegistrationPlate = "А123АА154" });

        Assert.Equal(1, await queries.GetVehiclesCountAsync(user.Id));
        Assert.Equal(0, await queries.GetVehiclesCountAsync(otherUser.Id));
        Assert.Equal(3, (await limits.GetVehicleLimitInfoAsync(user.Id)).MaxVehiclesForUser);
        await Assert.ThrowsAsync<Exception>(() => queries.GetById(otherUser.Id, vehicleId));
        await commands.Archive(user.Id, vehicleId);
        Assert.Equal(0, await queries.GetVehiclesCountAsync(user.Id));
        Assert.Equal(1, await queries.GetArchivedVehiclesCountAsync(user.Id));
        var archived = await queries.Get(user.Id, 1, 10, true);
        Assert.Equal(vehicleId, Assert.Single(archived.Items).Id);
        await commands.RestoreFromArchive(user.Id, vehicleId);
        await commands.TransferToOrganizationAsync(user.Id, vehicleId, organization.Id);

        Assert.Equal(0, await queries.GetVehiclesCountAsync(user.Id));
        Assert.Equal(1, await queries.GetVehiclesCountAsync(user.Id, organization.Id));
        await Assert.ThrowsAsync<Exception>(() => queries.GetById(user.Id, vehicleId));
        Assert.Equal(vehicleId, (await queries.GetById(user.Id, organization.Id, vehicleId)).Id);
        var organizationLimit = await limits.GetVehicleLimitInfoAsync(user.Id, organization.Id);
        Assert.Equal(2, organizationLimit.MaxVehiclesForUser);
        Assert.True(organizationLimit.CanAddMoreVehicles);
        await history.TrackDocumentGenerated(user.Id, organization.Id, vehicleId, "Сформирован документ", "PDF");
        var events = await queries.GetHistory(user.Id, organization.Id, vehicleId);

        Assert.Contains(events, item => item.EventType == "vehicle_created");
        Assert.Contains(events, item => item.EventType == "vehicle_archived");
        Assert.Contains(events, item => item.EventType == "vehicle_restored");
        Assert.Contains(events, item => item.EventType == "vehicle_transferred");
        Assert.Contains(events, item => item.EventType == "document_generated" && item.Description == "PDF");
        Assert.Equal(organization.Id, (await context.Vehicles.SingleAsync(item => item.Id == vehicleId)).OrganizationId);
    }

    [Fact]
    public async Task SharedLimitPreventsCreationAndRestorationWithoutChangingHistory()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var features = new Mock<IFeatureAccessService>();
        features.Setup(value => value.GetIntValueAsync(user.Id, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(1);
        var access = Mock.Of<IOrganizationSubscriptionService>();
        var repository = new VehicleRepository(context);
        var limits = new VehicleLimitQueryService(repository, Mock.Of<IPersonalSubscriptionQueryService>(), access, features.Object,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
        var commands = new VehicleCommandService(repository, new ModelRepository(context), clock.Object,
            new VehicleCatalogSuggestionRepository(context), context, access,
            new VehicleHistoryWriter(context, clock.Object, repository, access), limits);
        var archivedId = await commands.Create(user.Id, new CreateVehicleDto { RegistrationPlate = "А111АА154" });
        await commands.Archive(user.Id, archivedId);
        var activeId = await commands.Create(user.Id, new CreateVehicleDto { RegistrationPlate = "А222АА154" });
        var historyCount = await context.VehicleHistoryEvents.CountAsync(item => item.UserId == user.Id);

        Assert.False((await limits.GetVehicleLimitInfoAsync(user.Id)).CanAddMoreVehicles);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.Create(user.Id,
            new CreateVehicleDto { RegistrationPlate = "А333АА154" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.RestoreFromArchive(user.Id, archivedId));
        Assert.Equal(historyCount, await context.VehicleHistoryEvents.CountAsync(item => item.UserId == user.Id));
        Assert.True((await context.Vehicles.SingleAsync(item => item.Id == archivedId)).IsArchived);
        Assert.False((await context.Vehicles.SingleAsync(item => item.Id == activeId)).IsArchived);
    }
}
