using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleOptionQueryTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task OptionsPreserveScopeOrderLimitAndJsonContract()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { Name = "Варианты", OwnerUser = owner };
        var foreignOrganization = new Organization { Name = "Другая", OwnerUser = owner };
        var client = new OrganizationClient { Organization = organization, Name = " Клиент " };
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var vehicles = Enumerable.Range(0, 35).Select(index => new Vehicle
        {
            Organization = organization,
            User = owner,
            ManualBrandName = "Manual",
            ManualModelName = "Model",
            DateTimeCreation = now.AddMinutes(index),
            RegistrationPlate = $"A{index:000}AA"
        }).ToArray();
        vehicles[0].CurrentMileageUpdatedAt = now.AddDays(1);
        vehicles[0].OrganizationClient = client;
        vehicles[0].Name = " Рабочая техника ";
        vehicles[0].YearIssue = 2020;
        vehicles[0].CurrentMileage = 42;
        vehicles[0].MileageUnit = VehicleMileageUnit.EngineHours;
        context.Vehicles.AddRange(vehicles);
        context.Vehicles.AddRange(
            new Vehicle { Organization = foreignOrganization, User = owner, DateTimeCreation = now.AddDays(2) },
            new Vehicle { Organization = organization, User = owner, DateTimeCreation = now.AddDays(2), IsDeleted = true },
            new Vehicle { Organization = organization, User = owner, DateTimeCreation = now.AddDays(2), IsArchived = true },
            new Vehicle { User = owner, DateTimeCreation = now.AddDays(2) });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = new VehicleOptionQueryService(context);

        var minimumPage = await service.GetOptionsAsync(organization.Id, " ", null, 0);
        var maximumPage = await service.GetOptionsAsync(organization.Id, null, null, 100);
        var selectedClient = await service.GetOptionsAsync(organization.Id, null, client.Id, 20);

        Assert.Equal(5, minimumPage.Count);
        Assert.Equal(30, maximumPage.Count);
        Assert.Equal(vehicles[0].Id, minimumPage[0].Id);
        Assert.Equal(vehicles[34].Id, minimumPage[1].Id);
        Assert.All(maximumPage, option => Assert.Contains(option.Id, vehicles.Select(vehicle => vehicle.Id)));
        var option = Assert.Single(selectedClient);
        Assert.Equal(vehicles[0].Id, option.Id);
        Assert.Equal("Manual Model", option.DisplayName);
        Assert.Equal("Рабочая техника • 2020 г. • Клиент • A000AA", option.Subtitle);
        Assert.Equal(42, option.CurrentMileage);
        Assert.Equal("м/ч", option.MileageUnit);
        var json = JsonSerializer.SerializeToElement(option, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new[] { "id", "displayName", "subtitle", "currentMileage", "mileageUnit", "registrationPlate" },
            json.EnumerateObject().Select(property => property.Name));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchMatchesEveryIdentityFieldAndRequiresAllFirstFiveDistinctTokens()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { Name = "Поиск", OwnerUser = owner };
        var client = new OrganizationClient { Organization = organization, Name = "ClientNeedle" };
        var brand = new Brand { Name = $"BrandNeedle-{Guid.NewGuid():N}" };
        var model = new Model { Name = $"ModelNeedle-{Guid.NewGuid():N}", Brand = brand };
        var manual = new Vehicle
        {
            User = owner, Organization = organization, OrganizationClient = client,
            Name = "NameNeedle", ManualBrandName = "ManualBrandNeedle", ManualModelName = "ManualModelNeedle",
            RegistrationPlate = "PlateNeedle", Vin = "VinNeedle", BodyNumber = "BodyNeedle",
            EngineNumber = "EngineNeedle", RegistrationCertificate = "CertificateNeedle", DateTimeCreation = DateTime.UtcNow
        };
        var catalog = new Vehicle
        {
            User = owner, Organization = organization, Brand = brand, Model = model,
            Name = "Should not repeat catalog name", DateTimeCreation = DateTime.UtcNow
        };
        var empty = new Vehicle { User = owner, Organization = organization, DateTimeCreation = DateTime.UtcNow };
        context.Vehicles.AddRange(manual, catalog, empty);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = new VehicleOptionQueryService(context);

        foreach (var token in new[] { "nameNeedle", "manualBrandNeedle", "manualModelNeedle", "clientNeedle", "plateNeedle", "vinNeedle", "bodyNeedle", "engineNeedle", "certificateNeedle" })
        {
            var matches = await service.GetOptionsAsync(organization.Id, token.ToUpperInvariant(), null, 20);

            Assert.Contains(matches, option => option.Id == manual.Id);
            Assert.DoesNotContain(matches, option => option.Id == empty.Id);
        }

        foreach (var token in new[] { "brandNeedle", "modelNeedle" })
        {
            var matches = await service.GetOptionsAsync(organization.Id, token, null, 20);

            Assert.Contains(matches, option => option.Id == catalog.Id);
        }

        Assert.Single(await service.GetOptionsAsync(organization.Id, "NameNeedle NameNeedle VinNeedle PlateNeedle BodyNeedle EngineNeedle ignored", null, 20));
        Assert.Empty(await service.GetOptionsAsync(organization.Id, "NameNeedle missing", null, 20));
        var all = await service.GetOptionsAsync(organization.Id, null, null, 20);
        Assert.Equal($"{brand.Name} {model.Name}", Assert.Single(all, option => option.Id == catalog.Id).DisplayName);
        Assert.Null(Assert.Single(all, option => option.Id == catalog.Id).Subtitle);
        Assert.Equal("км", Assert.Single(all, option => option.Id == empty.Id).MileageUnit);
        Assert.Equal("Автомобиль", Assert.Single(all, option => option.Id == empty.Id).DisplayName);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
