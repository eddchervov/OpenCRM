using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleCommandValidationTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task UpdatePreservesUnchangedHistoricalPlateAndValidatesChangedPlateByVehicleKind()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var originalService = VehicleCommandValidationTests.CreateService(context);
        var vehicleId = await originalService.Create(user.Id, new CreateVehicleDto
        {
            Name = "Автомобиль",
            RegistrationPlate = "А123ВС1"
        });
        var service = VehicleCommandValidationTests.CreateService(context, registrationPlatePolicy: VehicleCommandValidationTests.CreatePlatePolicy());

        await service.Update(user.Id, vehicleId, new UpdateVehicleDto
        {
            Name = "Автомобиль",
            RegistrationPlate = "а 123-вс 1"
        });

        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicleId);
        Assert.Equal("А123ВС1", vehicle.RegistrationPlate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(user.Id, vehicleId, new UpdateVehicleDto
        {
            Name = "Автомобиль",
            RegistrationPlate = "А123ВС2"
        }));
        Assert.Equal("А123ВС1", vehicle.RegistrationPlate);
    }

    [Fact]
    public async Task CreatePreservesAlternatePlateForVehicleKindWithoutStrictFormat()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context, registrationPlatePolicy: VehicleCommandValidationTests.CreatePlatePolicy());

        var vehicleId = await service.Create(user.Id, new CreateVehicleDto
        {
            Kind = VehicleKind.Boat,
            Name = "Катер",
            RegistrationPlate = "AB-123"
        });

        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicleId);
        Assert.Equal("AB-123", vehicle.RegistrationPlate);

        await service.Update(user.Id, vehicleId, new UpdateVehicleDto
        {
            Kind = VehicleKind.Boat,
            Name = "Катер",
            RegistrationPlate = "АВ123"
        });

        Assert.Equal("АВ123", vehicle.RegistrationPlate);
    }

    [Theory]
    [InlineData("year", "1899")]
    [InlineData("year", "9999")]
    [InlineData("vin", "bad!")]
    [InlineData("plate", "!")]
    [InlineData("brand", "<b>")]
    [InlineData("model", "javascript:alert(1)")]
    [InlineData("name", "name>")]
    [InlineData("color", "<red")]
    [InlineData("description", "JAVASCRIPT:code")]
    public async Task InvalidInputDoesNotPersistVehicleOrHistory(string field, string value)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var dto = new CreateVehicleDto { RegistrationPlate = "А123АА154" };
        var property = field switch
        {
            "year" => nameof(CreateVehicleDto.YearIssue),
            "vin" => nameof(CreateVehicleDto.Vin),
            "plate" => nameof(CreateVehicleDto.RegistrationPlate),
            "brand" => nameof(CreateVehicleDto.ManualBrandName),
            "model" => nameof(CreateVehicleDto.ManualModelName),
            "name" => nameof(CreateVehicleDto.Name),
            "color" => nameof(CreateVehicleDto.Color),
            _ => nameof(CreateVehicleDto.Description)
        };
        typeof(CreateVehicleDto).GetProperty(property)!.SetValue(dto, field == "year" ? int.Parse(value) : value);
        var service = VehicleCommandValidationTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(user.Id, dto));
        Assert.False(await context.Vehicles.AnyAsync(item => item.UserId == user.Id));
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(item => item.UserId == user.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    [InlineData(100)]
    public async Task UpdatePreservesIdentityAndWritesReadableChangesForEveryKind(int kind)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context);
        var vehicleId = await service.Create(user.Id, new CreateVehicleDto { Name = "  Техника   1  " });
        var dto = new UpdateVehicleDto
        {
            Kind = (VehicleKind)kind,
            MileageUnit = VehicleMileageUnit.EngineHours,
            Name = " Техника  2 ",
            ManualBrandName = " Бренд ",
            ManualModelName = " Модель ",
            YearIssue = 2020,
            Vin = "ABC12345",
            RegistrationPlate = "А123АА154",
            BodyNumber = "Рама",
            EngineNumber = "Двигатель",
            Color = "Чёрный",
            RegistrationCertificate = "СТС",
            CommissioningDate = new DateTime(2020, 1, 2),
            Description = "Описание"
        };
        await service.Update(user.Id, vehicleId, dto);
        var historyCount = await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == vehicleId);
        await service.Update(user.Id, null, vehicleId, dto);

        Assert.Equal(historyCount, await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == vehicleId));
        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicleId);
        Assert.Equal("Техника 2", vehicle.Name);
        Assert.Equal((VehicleKind)kind, vehicle.Kind);
        var entry = await context.VehicleHistoryEvents.SingleAsync(item => item.VehicleId == vehicleId && item.EventType == "vehicle_updated");
        Assert.Contains("Название: Техника 1 -> Техника 2", entry.Description);
        Assert.Contains("Год выпуска: не указано -> 2020", entry.Description);
        Assert.Contains("Моточасы", entry.Description);
    }

    [Fact]
    public async Task ClientAndCatalogSelectionRejectForeignDeletedAndMismatchedRecords()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var foreignOrganization = new Organization { Name = "Другая студия", OwnerUser = user };
        var client = new OrganizationClient { Organization = organization, Name = "Клиент" };
        var deletedClient = new OrganizationClient { Organization = organization, Name = "Удалённый", IsDeleted = true };
        var foreignClient = new OrganizationClient { Organization = foreignOrganization, Name = "Чужой" };
        var wrongModel = new Model { Name = "Грузовая", Kind = VehicleKind.Truck, Brand = new Brand { Name = "Грузовые", Kind = VehicleKind.Truck } };
        var wrongBrand = new Model { Name = "Легковая", Brand = new Brand { Name = "Другие", Kind = VehicleKind.Truck } };
        context.AddRange(client, deletedClient, foreignClient, wrongModel, wrongBrand);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(user.Id, new CreateVehicleDto { Name = "Личная", OrganizationClientId = client.Id }));
        foreach (var clientId in new[] { Guid.NewGuid(), deletedClient.Id, foreignClient.Id })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(user.Id, organization.Id, new CreateVehicleDto { Name = "Техника", OrganizationClientId = clientId }));
        }

        foreach (var model in new[] { wrongModel, wrongBrand })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(user.Id, new CreateVehicleDto { Name = "Техника", ModelId = model.Id }));
        }

        var vehicleId = await service.Create(user.Id, organization.Id, new CreateVehicleDto { Name = "Техника", OrganizationClientId = client.Id });
        Assert.Equal(client.Id, (await context.Vehicles.SingleAsync(item => item.Id == vehicleId)).OrganizationClientId);
    }

    [Fact]
    public async Task RepeatedManualIdentityDoesNotDuplicateSuggestionOrHistory()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context);
        var brandName = "Бренд " + Guid.NewGuid();
        var vehicleId = await service.Create(user.Id, new CreateVehicleDto { Name = "Техника", ManualBrandName = brandName, ManualModelName = "Модель" });
        await service.Update(user.Id, vehicleId, new UpdateVehicleDto { Name = "Техника", ManualBrandName = brandName, ManualModelName = "Модель" });
        await service.Create(user.Id, new CreateVehicleDto { Name = "Другая", ManualBrandName = brandName, ManualModelName = "Модель" });

        Assert.Equal(1, await context.VehicleCatalogSuggestions.CountAsync(item => item.BrandName == brandName));
        Assert.Equal(1, await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == vehicleId));
        var model = new Model { Name = "Из справочника", Brand = new Brand { Name = "Справочник" } };
        context.Models.Add(model);
        await context.SaveChangesAsync();
        await service.Create(user.Id, new CreateVehicleDto { Name = "Из справочника", ManualBrandName = model.Brand.Name, ManualModelName = model.Name });
        Assert.False(await context.VehicleCatalogSuggestions.AnyAsync(item => item.BrandName == model.Brand.Name));
    }

    [Fact]
    public async Task MissingVehicleAndInvalidLifecycleDoNotWriteHistory()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context);
        var missingId = Guid.NewGuid();
        await Assert.ThrowsAsync<Exception>(() => service.Update(user.Id, missingId, new UpdateVehicleDto { Name = "Техника" }));
        await Assert.ThrowsAsync<Exception>(() => service.Archive(user.Id, missingId));
        await Assert.ThrowsAsync<Exception>(() => service.RestoreFromArchive(user.Id, missingId));
        await Assert.ThrowsAsync<Exception>(() => service.TransferToOrganizationAsync(user.Id, missingId, organization.Id));
        var vehicleId = await service.Create(user.Id, new CreateVehicleDto { Name = "Техника" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreFromArchive(user.Id, vehicleId));
        await service.Archive(user.Id, vehicleId);
        var count = await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == vehicleId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferToOrganizationAsync(user.Id, vehicleId, organization.Id));

        Assert.Equal(count, await context.VehicleHistoryEvents.CountAsync(item => item.VehicleId == vehicleId));
    }

    [Fact]
    public async Task CleanupDeletesOnlyVehiclesArchivedForAtLeastThirtyDays()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var old = new Vehicle { User = user, IsArchived = true, ArchiveDate = now.AddDays(-30), Name = "Старая" };
        var recent = new Vehicle { User = user, IsArchived = true, ArchiveDate = now.AddDays(-29), Name = "Новая" };
        var undated = new Vehicle { User = user, IsArchived = true, Name = "Без даты" };
        var active = new Vehicle { User = user, ArchiveDate = now.AddDays(-40), Name = "Активная" };
        context.AddRange(old, recent, undated, active);
        await context.SaveChangesAsync();
        await VehicleCommandValidationTests.CreateService(context).CleanUpArchive();
        context.ChangeTracker.Clear();

        Assert.True((await context.Vehicles.SingleAsync(item => item.Id == old.Id)).IsDeleted);
        Assert.False((await context.Vehicles.SingleAsync(item => item.Id == recent.Id)).IsDeleted);
        Assert.False((await context.Vehicles.SingleAsync(item => item.Id == undated.Id)).IsDeleted);
        Assert.False((await context.Vehicles.SingleAsync(item => item.Id == active.Id)).IsDeleted);
    }

    [Fact]
    public async Task CatalogCreationAndUnloadedIdentityUpdatePreserveHistoricalChanges()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var model = new Model { Name = "Модель", Brand = new Brand { Name = "Бренд" } };
        context.AddRange(user, model);
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context);
        var id = await service.Create(user.Id, new CreateVehicleDto
        {
            Name = "Техника", ModelId = model.Id, ManualBrandName = "Не использовать", ManualModelName = "Не использовать"
        });
        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == id);
        Assert.Equal(model.Id, vehicle.ModelId);
        Assert.Equal(model.BrandId, vehicle.BrandId);
        Assert.Null(vehicle.ManualBrandName);
        Assert.Null(vehicle.ManualModelName);
        context.ChangeTracker.Clear();
        await service.Update(user.Id, id, new UpdateVehicleDto { Name = "Ручная техника", ManualBrandName = "Другой бренд", ManualModelName = "Другая модель" });
        var history = await context.VehicleHistoryEvents.SingleAsync(item => item.VehicleId == id && item.EventType == "vehicle_updated");

        Assert.Contains("Название: Техника -> Ручная техника", history.Description);
        Assert.Contains("Другой бренд", history.Description);
        Assert.Contains("Другая модель", history.Description);
    }

    [Fact]
    public async Task TransferLimitRejectsMovementBeforeChangingVehicleOrHistory()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Name = "Техника" };
        var existing = new Vehicle { User = user, Organization = organization, Name = "Занятое место" };
        context.AddRange(organization, vehicle, existing);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => VehicleCommandValidationTests.CreateService(context, 1)
            .TransferToOrganizationAsync(user.Id, vehicle.Id, organization.Id));

        Assert.Null(vehicle.OrganizationId);
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public async Task FreeVehicleLimitCountsArchivedVehicles(int existingCount, bool canCreate)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Студия",
            OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        };
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.AddRange(Enumerable.Range(0, existingCount).Select(index => new Vehicle
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Name = $"Архивное авто {index}",
            IsArchived = true,
            DateTimeCreation = DateTime.UtcNow
        }));
        await context.SaveChangesAsync();
        var service = VehicleCommandValidationTests.CreateService(context, limit: 300, isFreePlan: true);

        if (canCreate)
        {
            await service.Create(user.Id, organization.Id, new CreateVehicleDto { Name = "Авто на границе" });
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.Create(user.Id, organization.Id, new CreateVehicleDto { Name = "Авто сверх границы" }));
            Assert.Contains("300 из 300", exception.Message, StringComparison.Ordinal);
        }

        context.ChangeTracker.Clear();
        Assert.Equal(existingCount + (canCreate ? 1 : 0),
            await context.Vehicles.CountAsync(vehicle => vehicle.OrganizationId == organization.Id && !vehicle.IsDeleted));
    }

    [Fact]
    public async Task ConcurrentFreeVehicleCreatesShareOrganizationCapacityLock()
    {
        await using var seedContext = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Студия",
            OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        };
        seedContext.Users.Add(user);
        seedContext.Organizations.Add(organization);
        seedContext.Vehicles.AddRange(Enumerable.Range(0, 299).Select(index => new Vehicle
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Name = $"Авто {index}",
            IsArchived = true,
            DateTimeCreation = DateTime.UtcNow
        }));
        await seedContext.SaveChangesAsync();

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstService = VehicleCommandValidationTests.CreateService(firstContext, limit: 300, isFreePlan: true);
        var secondService = VehicleCommandValidationTests.CreateService(secondContext, limit: 300, isFreePlan: true);
        var results = await Task.WhenAll(
            VehicleCommandValidationTests.TryCreateAsync(firstService, user.Id, organization.Id, "Первое новое авто"),
            VehicleCommandValidationTests.TryCreateAsync(secondService, user.Id, organization.Id, "Второе новое авто"));

        Assert.Single(results.Where(created => created));
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(300, await verificationContext.Vehicles.CountAsync(vehicle =>
            vehicle.OrganizationId == organization.Id && !vehicle.IsDeleted));
    }

    private static VehicleCommandService CreateService(
        OpenCRMContext context,
        int limit = 100,
        bool isFreePlan = false,
        IVehicleRegistrationPlatePolicy? registrationPlatePolicy = null)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        var features = new Mock<IFeatureAccessService>();
        features.Setup(value => value.GetIntValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(limit);
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(value => value.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>())).ReturnsAsync(limit);
        access.Setup(value => value.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = isFreePlan });
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var repository = new VehicleRepository(context);

        return new VehicleCommandService(repository, new ModelRepository(context), clock.Object,
            new VehicleCatalogSuggestionRepository(context), context, access.Object,
            new VehicleHistoryWriter(context, clock.Object, repository, access.Object),
            new VehicleLimitQueryService(repository, Mock.Of<IPersonalSubscriptionQueryService>(), access.Object, features.Object,
                Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true)),
            registrationPlatePolicy);
    }

    private static async Task<bool> TryCreateAsync(VehicleCommandService service, string userId, Guid organizationId, string name)
    {
        try
        {
            await service.Create(userId, organizationId, new CreateVehicleDto { Name = name });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
