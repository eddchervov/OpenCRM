using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleQueryServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task VehicleSearchMatchesLegacyLatinPlateByCyrillicPartialAndMixedQueries()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "plate-search@example.test" };
        var vehicle = VehicleQueryServiceTests.CreateVehicle(owner.Id, "Toyota", "Camry", "A123AA154", DateTime.UtcNow, VehicleKind.PassengerCar);
        context.Users.Add(owner);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var photos = new Mock<IVehiclePhotoRepository>();
        photos.Setup(repository => repository.GetByVehicleIdsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());
        var service = new VehicleQueryService(
            new VehicleRepository(context),
            photos.Object,
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<ISystemClock>(),
            VehicleQueryServiceTests.CreatePlatePolicy());

        var cyrillicPartial = await service.Get(owner.Id, 1, 10, false, searchQuery: "А1");
        var cyrillicPlate = await service.Get(owner.Id, 1, 10, false, searchQuery: "А123АА");
        var mixedPlate = await service.Get(owner.Id, 1, 10, false, searchQuery: "А123AA");

        Assert.Contains(cyrillicPartial.Items, item => item.Id == vehicle.Id);
        Assert.Contains(cyrillicPlate.Items, item => item.Id == vehicle.Id);
        Assert.Contains(mixedPlate.Items, item => item.Id == vehicle.Id);
        await context.Entry(vehicle).ReloadAsync();
        Assert.Equal("A123AA154", vehicle.RegistrationPlate);
    }

    [Fact]
    public async Task VehiclePageIncludesReadyMaintenanceAndBatchWorkProgress()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "vehicle-progress@example.test" };
        var organization = new Organization { Name = "Прогресс работ", OwnerUser = owner, OwnerUserId = owner.Id };
        var vehicle = CreateVehicle(owner.Id, "Toyota", "Camry", "P001PP", DateTime.UtcNow, VehicleKind.PassengerCar);
        vehicle.OrganizationId = organization.Id;
        vehicle.Organization = organization;
        var plannedVehicle = CreateVehicle(owner.Id, "Honda", "Civic", "P002PP", DateTime.UtcNow, VehicleKind.PassengerCar);
        plannedVehicle.OrganizationId = organization.Id;
        plannedVehicle.Organization = organization;
        var nearestPlannedDate = DateTime.UtcNow.Date.AddDays(1);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = owner.Id,
            User = owner,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc),
            ReadyForHandoverAt = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc),
            DateTimeCreation = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail { Name = "Полировка кузова", Position = 0 },
                new MaintenanceDetail { Name = "Керамическое покрытие", Position = 1 },
                new MaintenanceDetail { Name = "Полировальная паста", MaterialName = "Полировальная паста", MaterialQuantity = 1, Position = 2 }
            ]
        };
        var nearestPlannedMaintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = owner.Id,
            User = owner,
            VehicleId = plannedVehicle.Id,
            Vehicle = plannedVehicle,
            Status = MaintenanceStatus.Planned,
            Date = nearestPlannedDate,
            PlannedDate = nearestPlannedDate,
            DateTimeCreation = DateTime.UtcNow
        };
        var laterPlannedMaintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = owner.Id,
            User = owner,
            VehicleId = plannedVehicle.Id,
            Vehicle = plannedVehicle,
            Status = MaintenanceStatus.Planned,
            Date = nearestPlannedDate.AddDays(10),
            PlannedDate = nearestPlannedDate.AddDays(10),
            DateTimeCreation = DateTime.UtcNow
        };
        context.Users.Add(owner);
        context.Organizations.Add(organization);
        context.Vehicles.AddRange(vehicle, plannedVehicle);
        context.Maintenance.AddRange(maintenance, nearestPlannedMaintenance, laterPlannedMaintenance);
        context.MaintenanceWorkOperations.AddRange(
            new MaintenanceWorkOperation
            {
                Id = maintenance.Details[0].DetailId,
                MaintenanceId = maintenance.Id,
                Maintenance = maintenance,
                OrganizationId = organization.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Completed
            },
            new MaintenanceWorkOperation
            {
                Id = maintenance.Details[1].DetailId,
                MaintenanceId = maintenance.Id,
                Maintenance = maintenance,
                OrganizationId = organization.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Assigned
            },
            new MaintenanceWorkOperation
            {
                Id = Guid.NewGuid(),
                MaintenanceId = maintenance.Id,
                Maintenance = maintenance,
                OrganizationId = organization.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Completed
            });
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(owner.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var photos = new Mock<IVehiclePhotoRepository>();
        photos.Setup(repository => repository.GetByVehicleIdsAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());
        var service = new VehicleQueryService(
            new VehicleRepository(context),
            photos.Object,
            context,
            access.Object,
            Mock.Of<ISystemClock>(clock => clock.UtcNow == DateTime.UtcNow));

        var page = await service.Get(owner.Id, organization.Id, 1, 10, false, canViewMaintenance: true);
        var result = Assert.Single(page.Items, item => item.Id == vehicle.Id);
        var plannedResult = Assert.Single(page.Items, item => item.Id == plannedVehicle.Id);

        Assert.Equal(MaintenanceStatus.Completed, result.CurrentMaintenanceStatus);
        Assert.Equal(maintenance.Id, result.CurrentMaintenanceId);
        Assert.Equal("Полировка кузова", result.CurrentMaintenanceName);
        Assert.Equal(1, result.CompletedWorkCount);
        Assert.Equal(2, result.TotalWorkCount);
        Assert.Equal(nearestPlannedMaintenance.Id, plannedResult.CurrentMaintenanceId);

        var restrictedResult = Assert.Single(
            (await service.Get(owner.Id, organization.Id, 1, 10, false)).Items,
            item => item.Id == vehicle.Id);
        Assert.Null(restrictedResult.LastMaintenanceDate);
        Assert.Null(restrictedResult.CurrentMaintenanceId);
        Assert.Null(restrictedResult.CurrentMaintenanceName);
        Assert.Null(restrictedResult.CurrentMaintenanceStatus);
        Assert.Equal(0, restrictedResult.CompletedWorkCount);
        Assert.Equal(0, restrictedResult.TotalWorkCount);
    }

    [Fact]
    public async Task VehicleQueriesScopePersonalAndOrganizationRowsAndApplyFiltersAndPaging()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "owner@example.test" };
        var otherUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "other@example.test" };
        var organization = new Organization { Name = "Тестовая организация", OwnerUser = owner, OwnerUserId = owner.Id };
        var client = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Клиент Альфа" };
        context.Users.AddRange(owner, otherUser);
        context.Organizations.Add(organization);
        context.OrganizationClients.Add(client);
        var catalogBrand = new Brand { Name = "Acme", Kind = VehicleKind.Truck };
        var catalogModel = new Model { Name = "Hauler", Brand = catalogBrand, Kind = VehicleKind.Truck };
        context.Models.Add(catalogModel);
        await context.SaveChangesAsync();

        var personalVehicles = new[]
        {
            CreateVehicle(owner.Id, "Toyota", "Camry", "A001AA", DateTime.UtcNow.AddMinutes(-3), VehicleKind.PassengerCar),
            CreateVehicle(owner.Id, "Toyota", "Corolla", "A002AA", DateTime.UtcNow.AddMinutes(-2), VehicleKind.PassengerCar),
            CreateVehicle(owner.Id, "Honda", "Civic", "A003AA", DateTime.UtcNow.AddMinutes(-1), VehicleKind.Motorcycle)
        };
        var catalogVehicle = CreateVehicle(owner.Id, "", "", "A004AA", personalVehicles[0].DateTimeCreation.AddDays(-1), VehicleKind.Truck);
        catalogVehicle.BrandId = catalogBrand.Id;
        catalogVehicle.Brand = catalogBrand;
        catalogVehicle.ModelId = catalogModel.Id;
        catalogVehicle.Model = catalogModel;
        catalogVehicle.ManualBrandName = null;
        catalogVehicle.ManualModelName = null;
        var blankVehicle = CreateVehicle(owner.Id, "", "", "", personalVehicles[0].DateTimeCreation.AddDays(-2), VehicleKind.PassengerCar);
        blankVehicle.ManualBrandName = null;
        blankVehicle.ManualModelName = null;
        blankVehicle.Name = null;
        blankVehicle.RegistrationPlate = null;
        var archivedVehicle = CreateVehicle(owner.Id, "Old", "Model", "A005AA", personalVehicles[0].DateTimeCreation.AddDays(-3), VehicleKind.PassengerCar);
        archivedVehicle.IsArchived = true;
        var now = DateTime.UtcNow;
        archivedVehicle.ArchiveDate = now.AddDays(-40);
        var foreignVehicle = CreateVehicle(otherUser.Id, "Toyota", "Foreign", "B001BB", DateTime.UtcNow, VehicleKind.PassengerCar);
        var organizationVehicle = CreateVehicle(owner.Id, "Lada", "Granta", "C001CC", DateTime.UtcNow, VehicleKind.Truck);
        organizationVehicle.OrganizationId = organization.Id;
        organizationVehicle.Organization = organization;
        organizationVehicle.OrganizationClientId = client.Id;
        organizationVehicle.OrganizationClient = client;
        context.Vehicles.AddRange(personalVehicles.Append(catalogVehicle).Append(blankVehicle).Append(archivedVehicle).Append(foreignVehicle).Append(organizationVehicle));
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(owner.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var photos = new Mock<IVehiclePhotoRepository>();
        var photoId = Guid.NewGuid();
        photos.Setup(repository => repository.GetByVehicleIdsAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>
            {
                new() { Id = photoId, VehicleId = personalVehicles[0].Id, UserId = owner.Id, Path = "vehicle.jpg" }
            });
        photos.Setup(repository => repository.GetByVehicleIdAsync(owner.Id, null, personalVehicles[0].Id, false))
            .ReturnsAsync(new VehiclePhoto { Id = photoId, VehicleId = personalVehicles[0].Id, UserId = owner.Id, Path = "vehicle.jpg" });
        photos.Setup(repository => repository.GetByVehicleIdAsync(owner.Id, organization.Id, organizationVehicle.Id, false))
            .ReturnsAsync((VehiclePhoto)null!);
        var service = new VehicleQueryService(new VehicleRepository(context), photos.Object, context, access.Object, clock.Object);

        var personalPage = await service.Get(owner.Id, 1, 2, false);

        Assert.Equal(5, personalPage.AllCount);
        Assert.Equal(5, personalPage.TotalCount);
        Assert.Equal(1, personalPage.Page);
        Assert.Equal(2, personalPage.PageSize);
        Assert.Equal(personalVehicles[2].Id, personalPage.Items.First().Id);
        Assert.Equal(3, personalPage.UserBrands.Count());
        Assert.Equal(new[] { (int)VehicleKind.PassengerCar, (int)VehicleKind.Truck, (int)VehicleKind.Motorcycle }, personalPage.UserKinds);
        Assert.Equal(photoId, (await service.GetById(owner.Id, personalVehicles[0].Id)).PhotoId);
        var pageWithPhoto = await service.Get(owner.Id, 1, 10, false);
        Assert.Equal(photoId, pageWithPhoto.Items.Single(vehicle => vehicle.Id == personalVehicles[0].Id).PhotoId);
        Assert.Equal(5, await service.GetVehiclesCountAsync(owner.Id));
        Assert.Equal(1, await service.GetVehiclesCountAsync(otherUser.Id));
        Assert.Equal(1, await service.GetArchivedVehiclesCountAsync(owner.Id));

        var manualBrandPage = await service.Get(owner.Id, 1, 10, false, "manual:  Toyota ");
        Assert.Equal(2, manualBrandPage.TotalCount);
        Assert.All(manualBrandPage.Items, vehicle => Assert.Equal("Toyota", vehicle.BrandName));
        var catalogBrandPage = await service.Get(owner.Id, 1, 10, false, $"catalog:{catalogBrand.Id}");
        Assert.Equal(catalogVehicle.Id, Assert.Single(catalogBrandPage.Items).Id);
        var invalidCatalogFilter = await service.Get(owner.Id, 1, 10, false, "catalog:not-a-number");
        Assert.Equal(5, invalidCatalogFilter.TotalCount);
        var emptyManualFilter = await service.Get(owner.Id, 1, 10, false, "manual:   ");
        Assert.Equal(5, emptyManualFilter.TotalCount);

        var searchedPage = await service.Get(owner.Id, 1, 10, false, kind: (int)VehicleKind.PassengerCar, searchQuery: "A001AA Toyota Toyota");
        Assert.Equal(personalVehicles[0].Id, Assert.Single(searchedPage.Items).Id);

        var emptyPage = await service.Get(Guid.NewGuid().ToString(), 1, 10, false);
        Assert.Empty(emptyPage.Items);
        Assert.Empty(emptyPage.UserBrands);
        Assert.Empty(emptyPage.UserKinds);
        Assert.Equal(0, emptyPage.TotalCount);

        var organizationPage = await service.Get(owner.Id, organization.Id, 1, 10, false, organizationClientId: client.Id);
        var organizationResult = Assert.Single(organizationPage.Items);
        Assert.Equal(organizationVehicle.Id, organizationResult.Id);
        Assert.Equal(client.Name, organizationResult.OrganizationClientName);
        Assert.Equal(organization.Id, organizationResult.OrganizationId);
        var allOrganizationVehicles = await service.Get(owner.Id, organization.Id, 1, 10, false);
        Assert.Single(allOrganizationVehicles.Items);
        var personalIgnoresOrganizationClientFilter = await service.Get(owner.Id, null, 1, 10, false, organizationClientId: client.Id);
        Assert.Equal(5, personalIgnoresOrganizationClientFilter.TotalCount);
        Assert.Equal(organizationVehicle.Id, (await service.GetById(owner.Id, organization.Id, organizationVehicle.Id)).Id);
        var archivedPage = await service.Get(owner.Id, 1, 10, true);
        var archivedResult = Assert.Single(archivedPage.Items);
        Assert.Equal(archivedVehicle.Id, archivedResult.Id);
        Assert.Equal(0, archivedResult.DaysLeft);
        access.Verify(service => service.EnsureWorkspaceCanReadAsync(owner.Id, organization.Id, OrganizationPermissionCodes.VehiclesView), Times.AtLeastOnce);
    }

    [Fact]
    public async Task VehicleHistoryMergesEventsOrdersTiesAddsOrganizationLinksAndClampsTake()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "history@example.test" };
        var organization = new Organization { Name = "История", OwnerUser = user, OwnerUserId = user.Id };
        var vehicle = CreateVehicle(user.Id, "ВАЗ", "2107", "H001HH", DateTime.UtcNow, VehicleKind.PassengerCar);
        vehicle.OrganizationId = organization.Id;
        vehicle.Organization = organization;
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.Add(vehicle);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id,
            User = user,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            Date = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc),
            DateTimeCreation = DateTime.UtcNow,
            Type = OpenCRM.Domain.Enums.MaintenanceType.Other
        };
        context.Maintenance.Add(maintenance);
        var eventTime = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);
        context.VehicleHistoryEvents.AddRange(Enumerable.Range(0, 305).Select(index => new VehicleHistoryEvent
        {
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            OrganizationId = organization.Id,
            UserId = user.Id,
            User = user,
            EventType = "vehicle_updated",
            Title = $"Событие {index}",
            RelatedEntityType = "maintenance",
            RelatedEntityId = maintenance.Id,
            CreatedAt = eventTime.AddMinutes(index)
        }));
        context.VehicleHistoryEvents.AddRange(
            new VehicleHistoryEvent
            {
                VehicleId = vehicle.Id,
                Vehicle = vehicle,
                OrganizationId = organization.Id,
                UserId = user.Id,
                User = user,
                EventType = "external_event",
                Title = "Неизвестная ссылка",
                RelatedEntityType = "unknown",
                RelatedEntityId = Guid.NewGuid(),
                CreatedAt = eventTime.AddMinutes(306)
            },
            new VehicleHistoryEvent
            {
                VehicleId = vehicle.Id,
                Vehicle = vehicle,
                OrganizationId = organization.Id,
                UserId = user.Id,
                User = user,
                EventType = "without_link",
                Title = "Без ссылки",
                RelatedEntityType = "maintenance",
                RelatedEntityId = null,
                CreatedAt = eventTime.AddMinutes(307)
            });
        context.MaintenanceHistoryEvents.Add(new MaintenanceHistoryEvent
        {
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            VehicleId = vehicle.Id,
            OrganizationId = organization.Id,
            UserId = user.Id,
            User = user,
            EventType = "completed",
            Title = "Работа завершена",
            CreatedAt = eventTime.AddMinutes(308)
        });
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(user.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var service = new VehicleQueryService(
            new VehicleRepository(context),
            Mock.Of<IVehiclePhotoRepository>(),
            context,
            access.Object,
            Mock.Of<ISystemClock>());

        var minimumTake = await service.GetHistory(user.Id, organization.Id, vehicle.Id, 0);
        Assert.Equal(20, minimumTake.Count);
        Assert.Equal("maintenance_completed", minimumTake[0].EventType);
        Assert.Equal($"/maintenance/{maintenance.Id}?organizationId={organization.Id:D}", minimumTake[0].RelatedUrl);
        Assert.Null(Assert.Single(minimumTake, item => item.EventType == "external_event").RelatedUrl);
        Assert.Null(Assert.Single(minimumTake, item => item.EventType == "without_link").RelatedUrl);

        var maximumTake = await service.GetHistory(user.Id, organization.Id, vehicle.Id, 500);
        Assert.Equal(300, maximumTake.Count);
        Assert.Equal("maintenance_completed", maximumTake[0].EventType);
        Assert.Equal(eventTime.AddMinutes(8), maximumTake[^1].CreatedAt);
        Assert.Equal($"/maintenance/{maintenance.Id}?organizationId={organization.Id:D}", maximumTake[3].RelatedUrl);
        await Assert.ThrowsAsync<Exception>(() => service.GetHistory(user.Id, organization.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ArchivedVehicleDaysLeftUsesTheSameClockForSingleAndPagedQueries()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "archive@example.test" };
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var datedVehicle = CreateVehicle(user.Id, "Toyota", "Camry", "A001AA", now, VehicleKind.PassengerCar);
        datedVehicle.IsArchived = true;
        datedVehicle.ArchiveDate = now.AddDays(-29).AddHours(-23).AddMinutes(-59);
        var undatedVehicle = CreateVehicle(user.Id, "Toyota", "Corolla", "A002AA", now.AddMinutes(-1), VehicleKind.PassengerCar);
        undatedVehicle.IsArchived = true;
        context.Users.Add(user);
        context.Vehicles.AddRange(datedVehicle, undatedVehicle);
        await context.SaveChangesAsync();

        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var photos = new Mock<IVehiclePhotoRepository>();
        photos.Setup(repository => repository.GetByVehicleIdsAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());
        var service = new VehicleQueryService(
            new VehicleRepository(context),
            photos.Object,
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            clock.Object);

        var singleVehicle = await service.GetById(user.Id, datedVehicle.Id);
        var archivedVehicles = await service.Get(user.Id, 1, 10, true);

        Assert.Equal(1, singleVehicle.DaysLeft);
        Assert.Equal(1, Assert.Single(archivedVehicles.Items, vehicle => vehicle.Id == datedVehicle.Id).DaysLeft);
        Assert.Null(Assert.Single(archivedVehicles.Items, vehicle => vehicle.Id == undatedVehicle.Id).DaysLeft);
    }

    [Fact]
    public async Task HistoryKeepsPersonalMaintenanceUrlWithoutOrganizationSuffix()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString() };
        var vehicle = CreateVehicle(user.Id, "Toyota", "Corolla", "P001PP", DateTime.UtcNow, VehicleKind.PassengerCar);
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        var maintenanceId = Guid.NewGuid();
        context.VehicleHistoryEvents.AddRange(
            new VehicleHistoryEvent
            {
                VehicleId = vehicle.Id,
                Vehicle = vehicle,
                UserId = user.Id,
                User = user,
                EventType = "maintenance_link",
                Title = "Связанное обслуживание",
                RelatedEntityType = "maintenance",
                RelatedEntityId = maintenanceId,
                CreatedAt = DateTime.UtcNow
            },
            new VehicleHistoryEvent
            {
                VehicleId = vehicle.Id,
                Vehicle = vehicle,
                UserId = user.Id,
                User = user,
                EventType = "unrelated",
                Title = "Событие без ссылки",
                RelatedEntityType = "vehicle",
                RelatedEntityId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await context.SaveChangesAsync();
        var service = new VehicleQueryService(
            new VehicleRepository(context),
            Mock.Of<IVehiclePhotoRepository>(),
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<ISystemClock>());

        var events = await service.GetHistory(user.Id, vehicle.Id);

        Assert.Equal($"/maintenance/{maintenanceId}", events[0].RelatedUrl);
        Assert.Null(events[1].RelatedUrl);
    }

    private static Vehicle CreateVehicle(
        string userId,
        string brand,
        string model,
        string registrationPlate,
        DateTime createdAt,
        VehicleKind kind)
    {
        return new Vehicle
        {
            UserId = userId,
            Kind = kind,
            ManualBrandName = brand,
            ManualModelName = model,
            RegistrationPlate = registrationPlate,
            Name = $"{brand} {model}",
            DateTimeCreation = createdAt
        };
    }

    private static IVehicleRegistrationPlatePolicy CreatePlatePolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VehicleRegistrationPlates:MaxLength"] = "16",
                ["VehicleRegistrationPlates:RegexTimeoutMilliseconds"] = "100",
                ["VehicleRegistrationPlates:MaxSearchVariants"] = "64",
                ["VehicleRegistrationPlates:LetterPattern"] = "[АВЕКМНОРСТУХ]",
                ["VehicleRegistrationPlates:CharacterMappings:A"] = "А",
                ["VehicleRegistrationPlates:Formats:0:Code"] = "car",
                ["VehicleRegistrationPlates:Formats:0:Kinds:0"] = "1",
                ["VehicleRegistrationPlates:Formats:0:Masks:0"] = "L###LL###",
                ["VehicleRegistrationPlates:Formats:0:Pattern"] = "^[АВЕКМНОРСТУХ][0-9]{3}[АВЕКМНОРСТУХ]{2}[0-9]{3}$",
                ["VehicleRegistrationPlates:Formats:0:Example"] = "А123ВС154"
            })
            .Build();

        return new VehicleRegistrationPlatePolicy(configuration);
    }
}
