using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Queries;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleIdentityTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task UnknownVehicleCanBeReadEditedAndEnrichedWithoutLosingItsHistory()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleIdentityTests.CreateService(context);
        var id = await service.Create(user.Id, new CreateVehicleDto { RegistrationPlate = "А667РО142" });
        var saved = await context.Vehicles.SingleAsync(vehicle => vehicle.Id == id);
        Assert.Null(saved.ModelId);
        Assert.Null(saved.ManualBrandName);
        Assert.Null(saved.ManualModelName);
        Assert.Null(saved.Name);
        Assert.False(await context.VehicleCatalogSuggestions.AnyAsync(item => item.SuggestedByUserId == user.Id));

        var dto = await VehicleIdentityTests.CreateQueryService(context).GetById(user.Id, id);
        Assert.Equal("Автомобиль · А667РО142", dto.DisplayName);
        Assert.Empty(dto.BrandName);
        Assert.Empty(dto.ModelName);
        var list = await VehicleIdentityTests.CreateQueryService(context).Get(user.Id, 1, 20, false, searchQuery: "А667РО142");
        Assert.Contains(list.Items, vehicle => vehicle.Id == id && vehicle.DisplayName == dto.DisplayName);
        var work = new OpenCRM.Domain.Maintenance
        {
            VehicleId = id, UserId = user.Id, Date = DateTime.UtcNow,
            PlannedDate = DateTime.UtcNow.Date, DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance, Details = []
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        await service.Update(user.Id, id, new UpdateVehicleDto { RegistrationPlate = "А667РО142", Color = "Чёрный" });
        Assert.Null(saved.ManualModelName);

        var catalogModel = new Model { Name = "Модель теста", Brand = new Brand { Name = "Марка теста" } };
        context.Models.Add(catalogModel);
        await context.SaveChangesAsync();
        await service.Update(user.Id, id, new UpdateVehicleDto { RegistrationPlate = "А667РО142", ModelId = catalogModel.Id });
        context.ChangeTracker.Clear();
        dto = await VehicleIdentityTests.CreateQueryService(context).GetById(user.Id, id);
        Assert.Equal(catalogModel.Id, dto.ModelId);
        Assert.Equal("Марка теста Модель теста", dto.DisplayName);
        Assert.Null(dto.ManualBrandName);
        Assert.Equal(id, (await context.Maintenance.SingleAsync(item => item.Id == work.Id)).VehicleId);
        Assert.True(await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == id) >= 3);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("Марка", null, false)]
    [InlineData(null, "Модель", false)]
    [InlineData("Марка", "Модель", true)]
    public async Task OnlyCompleteManualIdentityCreatesCatalogSuggestion(string? brand, string? model, bool expectedSuggestion)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleIdentityTests.CreateService(context);
        var id = await service.Create(user.Id, new CreateVehicleDto
        {
            RegistrationPlate = "А123АА154", ManualBrandName = brand, ManualModelName = model
        });
        var saved = await context.Vehicles.SingleAsync(item => item.Id == id);
        Assert.Equal(brand, saved.ManualBrandName);
        Assert.Equal(model, saved.ManualModelName);
        Assert.Equal(expectedSuggestion, await context.VehicleCatalogSuggestions.AnyAsync(item => item.SuggestedByUserId == user.Id));
    }

    [Fact]
    public async Task StillRejectsMissingIdentifierAndInvalidCatalogModel()
    {
        await using var context = fixture.CreateContext();
        var service = VehicleIdentityTests.CreateService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create("test", new CreateVehicleDto()));
        await Assert.ThrowsAnyAsync<Exception>(() => service.Create("test", new CreateVehicleDto
        {
            RegistrationPlate = "А123АА154", ModelId = int.MaxValue
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickBookingPreservesUnknownIdentityAndReusesVehicleByPlate(bool autoService)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Тест", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var organizationService = new Mock<IOrganizationQueryService>();
        organizationService.Setup(service => service.GetByIdAsync(user.Id, organization.Id)).ReturnsAsync(new OrganizationDto
        {
            Id = organization.Id, PrimaryScenarioCode = autoService ? "service-station" : "detailing"
        });
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        var catalog = new Mock<ICatalogQueryService>();
        var serviceId = Guid.NewGuid();
        catalog.Setup(service => service.GetActiveServiceAsync(user.Id, organization.Id, serviceId))
            .ReturnsAsync(new OrganizationScenarioCatalogItemDto { Id = serviceId, Name = "Полировка" });
        var maintenance = new Mock<IMaintenanceCreateService>();
        maintenance.Setup(service => service.Create(user.Id, organization.Id, It.IsAny<Guid>(), It.IsAny<CreateMaintenanceDto>()))
            .ReturnsAsync(Guid.NewGuid());
        var vehicleService = VehicleIdentityTests.CreateService(context);
        var clientLookup = Mock.Of<IQuickBookingClientLookupService>();
        var vehicleLookup = new QuickBookingVehicleLookupService(context, subscriptions.Object);
        Guid firstId;
        Guid secondId;
        if (autoService)
        {
            var service = new OrganizationAutoServiceQuickBookingService(clientLookup, vehicleLookup, organizationService.Object, subscriptions.Object,
                Mock.Of<IOrganizationClientCommandService>(), vehicleService, maintenance.Object, catalog.Object);
            var request = new CreateOrganizationAutoServiceQuickBookingDto { RegistrationPlate = "А667РО142", ServiceId = serviceId, PlannedDate = DateTime.UtcNow.Date };
            firstId = (await service.CreateAsync(user.Id, organization.Id, request)).VehicleId;
            secondId = (await service.CreateAsync(user.Id, organization.Id, request)).VehicleId;
        }
        else
        {
            var service = new OrganizationDetailingQuickBookingService(context, clientLookup, vehicleLookup, organizationService.Object, subscriptions.Object,
                Mock.Of<IOrganizationClientCommandService>(), vehicleService, maintenance.Object, catalog.Object);
            var request = new CreateOrganizationDetailingQuickBookingDto { RegistrationPlate = "А667РО142", ServiceId = serviceId, PlannedDate = DateTime.UtcNow.Date };
            firstId = (await service.CreateAsync(user.Id, organization.Id, request)).VehicleId;
            secondId = (await service.CreateAsync(user.Id, organization.Id, request)).VehicleId;
        }

        Assert.Equal(firstId, secondId);
        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == firstId);
        Assert.Null(vehicle.ManualBrandName);
        Assert.Null(vehicle.ManualModelName);
        Assert.Null(vehicle.Name);
        Assert.False(await context.VehicleCatalogSuggestions.AnyAsync(item => item.SuggestedByUserId == user.Id));
        var options = await new VehicleOptionQueryService(context).GetOptionsAsync(organization.Id, null, null, 20);
        Assert.Contains(options.Select(item => System.Text.Json.JsonSerializer.SerializeToElement(item)),
            item => item.GetProperty("Id").GetGuid() == firstId && item.GetProperty("DisplayName").GetString() == "Автомобиль · А667РО142");
    }

    [Fact]
    public async Task VehicleJournalPreservesMaintenanceChangeDetails()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleIdentityTests.CreateService(context);
        var vehicleId = await service.Create(user.Id, new CreateVehicleDto { RegistrationPlate = "А123АА142" });
        var work = new OpenCRM.Domain.Maintenance
        {
            VehicleId = vehicleId,
            UserId = user.Id,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Details = []
        };

        var historyEvent = new MaintenanceHistoryEvent
        {
            Maintenance = work,
            VehicleId = vehicleId,
            UserId = user.Id,
            EventType = "fields_changed",
            Title = "Изменены сведения обслуживания",
            Description = "Место: Пост 1 -> Пост 2\nСтоимость: 1000 -> 1500",
            ActorDisplayName = "Администратор",
            CreatedAt = DateTime.UtcNow
        };

        context.MaintenanceHistoryEvents.Add(historyEvent);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var history = await VehicleIdentityTests.CreateQueryService(context).GetHistory(user.Id, vehicleId);
        var entry = Assert.Single(history, item => item.Id == historyEvent.Id);
        Assert.Equal(historyEvent.Title, entry.Title);
        Assert.Equal(historyEvent.Description, entry.Description);
        Assert.Equal(historyEvent.ActorDisplayName, entry.ActorDisplayName);
        Assert.Equal(work.Id, entry.RelatedEntityId);
        Assert.Equal($"/maintenance/{work.Id}", entry.RelatedUrl);
    }

    private static VehicleCommandService CreateService(OpenCRMContext context)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var features = new Mock<IFeatureAccessService>();
        features.Setup(value => value.GetIntValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(100);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>())).ReturnsAsync(100);

        var repository = new VehicleRepository(context);
        return new VehicleCommandService(repository, new ModelRepository(context), clock.Object,
            new VehicleCatalogSuggestionRepository(context), context, subscriptions.Object,
            new VehicleHistoryWriter(context, clock.Object, repository, subscriptions.Object),
            new VehicleLimitQueryService(repository, Mock.Of<IPersonalSubscriptionQueryService>(), subscriptions.Object, features.Object,
                Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true)));
    }
    private static VehicleQueryService CreateQueryService(OpenCRMContext context)
    {
        var photos = new Mock<IVehiclePhotoRepository>();
        photos.Setup(value => value.GetByVehicleIdsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());

        return new VehicleQueryService(new VehicleRepository(context), photos.Object, context,
            Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<ISystemClock>());
    }
}
